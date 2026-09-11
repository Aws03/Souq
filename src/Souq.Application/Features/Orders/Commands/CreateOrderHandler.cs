using MediatR;
using Microsoft.Extensions.Logging;
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Models;
using Souq.Application.Common.Security;
using Souq.Application.Common.Tenancy;
using Souq.Application.Features.Baskets.Contracts;
using Souq.Application.Features.Inventory.Contracts;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Orders.Commands;

// ============================================================================
// CreateOrderHandler — "منسّق" حالة الاستخدام الأهم في النظام.
//
// (1) تحقّق بلا أثر: الأسعار والخصم من خطّ التسعير الواحد (IPricing، المرحلة 8) — الخطّ نفسه الذي تُعرض به السلة،
//     فإجمالي الدفع هو إجمالي السلة. كل سطر قابل للبيع، المتاح يكفي (قراءة مبكرة لرسالة واضحة)، والكوبون مقبول.
// (2) الطلب Pending والحجز في معاملة واحدة (المرحلة 6، ADR-0026): حفظ الطلب يولّد معرّفه، ثم يحجز Inventory
//     الأسطر بمرجعه ("order:{id}"). نقص حقيقي بعد القراءة (سباق) ⇒ InsufficientStock وتُلغى المعاملة كلها — لا
//     طلب بلا حجز ولا حجز بلا طلب. المخزون المحجوز لا يُسجَّل بيعاً إلا عند الدفع.
// (3) نيّة الدفع خارج أي معاملة (ADR-0021)؛ فشل البوّابة ⇒ تعويض فوري: إلغاء الطلب وتحرير الحجز (Phase 0 C6).
//     طلب هُجر بعد ذلك يلتقطه منسّق انتهاء المهلة (ExpireStaleCheckouts).
// ============================================================================
public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, Result<OrderCreatedDto>>
{
    private const string PaymentStartFailedNote = "تعذّر بدء عملية الدفع";

    private readonly IOrderRepository _orders;
    private readonly ICustomerRepository _customers;
    private readonly IPricing _pricing;
    private readonly IInventoryReservations _reservations;
    private readonly IStockAvailability _availability;
    private readonly IPaymentService _payment;
    private readonly OrderPaymentConfirmation _confirmation;
    private readonly ICurrentUser _currentUser;
    private readonly ITenantContext _tenant;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<CreateOrderHandler> _logger;

    public CreateOrderHandler(
        IOrderRepository orders, ICustomerRepository customers, IPricing pricing,
        IInventoryReservations reservations, IStockAvailability availability, IPaymentService payment,
        OrderPaymentConfirmation confirmation, ICurrentUser currentUser, ITenantContext tenant, IUnitOfWork uow,
        ILogger<CreateOrderHandler> logger)
    {
        _orders = orders; _customers = customers; _pricing = pricing;
        _reservations = reservations; _availability = availability; _payment = payment; _confirmation = confirmation;
        _currentUser = currentUser; _tenant = tenant; _uow = uow; _logger = logger;
    }

    public async Task<Result<OrderCreatedDto>> Handle(CreateOrderCommand cmd, CancellationToken ct)
    {
        // العميل هو المستخدم الحالي دائماً — الأمر لا يحمل معرّف عميل يمكن التلاعب به (B7).
        var customerId = _currentUser.RequireCustomerId();
        var customer = await _customers.GetByIdAsync(customerId, ct);
        if (customer is null)
            // توكن صالح لحساب لم يعد موجوداً ⇒ الهوية نفسها لم تعد صالحة (401 ⇒ إعادة دخول).
            return Result<OrderCreatedDto>.Failure(Error.Unauthorized("CustomerNotFound", "العميل غير موجود"));

        // المحظور تجارياً (المرحلة 7) يرى حسابه وطلباته لكنه لا يطلب.
        if (customer.IsBlocked)
            return Result<OrderCreatedDto>.Failure(Error.Forbidden("CustomerBlocked", "حسابك موقوف عن الشراء في هذا المتجر."));

        // عنوان من دفتر العميل نفسه (لقطة نصّية على الطلب)، أو النصّ المُرسَل.
        var shippingAddress = cmd.ShippingAddress ?? "";
        if (cmd.ShippingAddressId is int addressId)
        {
            var saved = customer.Addresses.FirstOrDefault(a => a.Id == addressId);
            if (saved is null)
                return Result<OrderCreatedDto>.Failure(Error.Validation("AddressNotFound", "العنوان غير موجود في دفترك"));
            shippingAddress = saved.ToPostalAddress().ToSingleLine(Order.ShippingAddressMaxLength);
        }

        var store = _tenant.RequireTenant();

        // (1) التسعير: كل سطر من الكتالوج الحيّ لهذا المتجر (منتج غير منشور أو من متجر آخر غير قابل للبيع).
        var quote = await _pricing.QuoteAsync(cmd.Items.Select(i => new PricingLine(i.ProductId, i.Quantity)).ToList(), cmd.CouponCode, ct);
        if (quote.Lines.FirstOrDefault(l => !l.Sellable) is { } unsellable)
            return Result<OrderCreatedDto>.Failure(Error.Validation("ProductNotFound", $"المنتج رقم {unsellable.ProductId} غير متاح"));

        var available = await _availability.AvailableAsync(quote.Lines.Select(l => l.VariantId).Distinct().ToList(), ct);
        foreach (var group in quote.Lines.GroupBy(l => l.VariantId))
        {
            var requested = group.Sum(l => l.Quantity);
            var inStock = available.GetValueOrDefault(group.Key);
            if (requested > inStock)
                return Result<OrderCreatedDto>.Failure(Error.BusinessRule("InsufficientStock",
                    $"الكمية المطلوبة ({requested}) من \"{group.First().Name}\" غير متوفرة. المتاح: {Math.Max(inStock, 0)}"));
        }

        // كوبون مرفوض يوقف الطلب برمزه (ModuleDisabled، CouponNotFound، InvalidCoupon — 422) قبل أي كتابة.
        if (quote.Coupon is { Applied: false } rejected)
            return Result<OrderCreatedDto>.Failure(Error.BusinessRule(rejected.ErrorCode!, rejected.Message!));

        // (2) الطلب بعملة المتجر ولقطات أسطر التسعير (الاسم بلغة المتجر الافتراضية — الفاتورة تبقى كما كانت لحظة
        //     الشراء)، ثم الحجز.
        var order = new Order(customerId, shippingAddress, store.Currency);
        foreach (var line in quote.Lines)
            order.AddItem(line.ProductId, line.Name, line.UnitPrice, line.Quantity);
        if (quote.Coupon is { Applied: true } applied)
            order.ApplyCoupon(applied.Code, quote.Discount);

        var reservationLines = quote.Lines.Select(l => new ReservationLine(l.VariantId, l.Quantity, l.Name)).ToList();
        await _orders.AddAsync(order, ct);
        await _uow.InTransactionAsync(async () =>
        {
            await _uow.SaveChangesAsync(ct);
            await _reservations.ReserveAsync(OrderStockReference.For(order.Id), reservationLines, ct);
        }, ct);

        // (3) نيّة دفع لدى البوّابة مربوطة بالطلب. فشل البوّابة نفسها (انقطاع، مفتاح خاطئ) ⇒ تعويض فوري.
        PaymentIntentResult intent;
        try
        {
            intent = await _payment.CreateIntentAsync(order.TotalAmount, order.Id.ToString(), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "فشل إنشاء نيّة الدفع للطلب {OrderId} — أُلغي الطلب وحُرّر حجزه", order.Id);
            await _confirmation.CancelAsync(order, PaymentStartFailedNote, expired: false, ct);
            return Result<OrderCreatedDto>.Failure(Error.Unavailable(
                "PaymentUnavailable", "تعذّر بدء عملية الدفع حالياً. لم يُحجز أي مخزون، يُرجى المحاولة لاحقاً."));
        }

        order.SetPaymentIntent(intent.PaymentIntentId);
        await _uow.SaveChangesAsync(ct);

        return Result<OrderCreatedDto>.Success(new OrderCreatedDto(
            order.Id, order.Status.ToString(),
            order.Subtotal.Amount, order.DiscountAmount?.Amount, order.TotalAmount.Amount, order.TotalAmount.Currency,
            intent.ClientSecret));
    }
}
