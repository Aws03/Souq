using MediatR;
using Microsoft.Extensions.Logging;
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Models;
using Souq.Application.Common.Security;
using Souq.Application.Common.Tenancy;
using Souq.Application.Features.Baskets.Contracts;
using Souq.Application.Features.Coupons.Contracts;
using Souq.Application.Features.Payments.Contracts;
using Souq.Application.Features.Inventory.Contracts;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Features.Orders.Commands;

// ============================================================================
// CreateOrderHandler — "منسّق" حالة الاستخدام الأهم في النظام.
//
// (1) تحقّق بلا أثر: الأسطر المُرسَلة أو سلة العميل (المرحلة 9)، مسعَّرةً بخطّ التسعير الواحد (IPricing، المرحلة 8) —
//     الخطّ نفسه الذي تُعرض به السلة، فإجمالي الدفع هو إجمالي السلة. كل سطر قابل للبيع، المتاح يكفي (قراءة مبكرة
//     لرسالة واضحة)، والكوبون مقبول. العنوانان لقطتان من دفتر العميل نفسه أو النصّ المُرسَل.
// (2) معاملة واحدة (المرحلة 6، ADR-0026): رقم الطلب من عدّاد المتجر (المرحلة 9)، التثبيت (تجميد الأسطر والإجماليات)،
//     حفظ الطلب، ثم حجز Inventory بمرجعه ("order:{id}"). نقص حقيقي بعد القراءة (سباق) ⇒ InsufficientStock وتُلغى
//     المعاملة كلها — لا طلب بلا حجز ولا حجز بلا طلب ولا رقم مستهلك. المحجوز لا يُسجَّل بيعاً إلا عند الدفع.
// (3) نيّة الدفع خارج أي معاملة (ADR-0021)؛ فشل البوّابة ⇒ تعويض فوري: إلغاء الطلب وتحرير الحجز (Phase 0 C6).
//     طلب هُجر بعد ذلك يلتقطه منسّق انتهاء المهلة (ExpireStaleCheckouts). السلة تُستهلك عند تأكيد الدفع لا هنا.
// ============================================================================
public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, Result<OrderCreatedDto>>
{
    private const string PaymentStartFailedNote = "تعذّر بدء عملية الدفع";

    private readonly IOrderRepository _orders;
    private readonly ICustomerRepository _customers;
    private readonly IPricing _pricing;
    private readonly IBasketCheckout _baskets;
    private readonly IOrderNumbers _numbers;
    private readonly ICouponRedemptions _couponRedemptions;
    private readonly IOrderPayments _orderPayments;
    private readonly IInventoryReservations _reservations;
    private readonly IStockAvailability _availability;
    private readonly IPaymentService _payment;
    private readonly OrderPaymentConfirmation _confirmation;
    private readonly ICurrentUser _currentUser;
    private readonly ITenantContext _tenant;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;
    private readonly ILogger<CreateOrderHandler> _logger;

    public CreateOrderHandler(
        IOrderRepository orders, ICustomerRepository customers, IPricing pricing, IBasketCheckout baskets, IOrderNumbers numbers,
        ICouponRedemptions couponRedemptions, IOrderPayments orderPayments, IInventoryReservations reservations,
        IStockAvailability availability,
        IPaymentService payment, OrderPaymentConfirmation confirmation, ICurrentUser currentUser, ITenantContext tenant,
        IUnitOfWork uow, TimeProvider clock, ILogger<CreateOrderHandler> logger)
    {
        _orders = orders; _customers = customers; _pricing = pricing; _baskets = baskets; _numbers = numbers;
        _couponRedemptions = couponRedemptions; _orderPayments = orderPayments;
        _reservations = reservations; _availability = availability; _payment = payment; _confirmation = confirmation;
        _currentUser = currentUser; _tenant = tenant; _uow = uow; _clock = clock; _logger = logger;
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

        // العنوانان لقطتان: من دفتر العميل نفسه (معرّف عنوان غيره ⇒ غير موجود) أو النصّ المُرسَل للشحن. الفوترة بلا
        // اختيار ⇒ عنوان الفوترة الافتراضي في الدفتر، وإلا عنوان الشحن نفسه (يقرّره الكيان).
        var shippingAddress = cmd.ShippingAddress ?? "";
        if (cmd.ShippingAddressId is int shippingId)
        {
            if (FromBook(customer, shippingId) is not { } saved) return AddressNotFound();
            shippingAddress = saved;
        }
        var billingAddress = customer.Addresses.FirstOrDefault(a => a.IsDefaultBilling) is { } defaultBilling
            ? FromBook(customer, defaultBilling.Id)
            : null;
        if (cmd.BillingAddressId is int billingId)
        {
            if (FromBook(customer, billingId) is not { } saved) return AddressNotFound();
            billingAddress = saved;
        }

        var store = _tenant.RequireTenant();

        // (1) الأسطر: المُرسَلة، وإلا سلة العميل. ثم التسعير: كل سطر من الكتالوج الحيّ لهذا المتجر (منتج غير منشور أو من
        //     متجر آخر غير قابل للبيع).
        var lines = cmd.Items is { Count: > 0 }
            ? cmd.Items.Select(i => new PricingLine(i.ProductId, i.Quantity)).ToList()
            : await _baskets.LinesForCustomerAsync(customerId, ct);
        if (lines.Count == 0)
            return Result<OrderCreatedDto>.Failure(Error.Validation("BasketEmpty", "السلة فارغة — أضف منتجات قبل إتمام الطلب"));

        var quote = await _pricing.QuoteAsync(lines, cmd.CouponCode, customerId, ct);
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

        // (2) الطلب بعملة المتجر ولقطات أسطر التسعير (الاسم بلغة المتجر الافتراضية — الفاتورة تبقى كما كانت لحظة الشراء).
        var order = new Order(customerId, shippingAddress, store.Currency, billingAddress);
        foreach (var line in quote.Lines)
            order.AddItem(line.ProductId, line.Name, line.UnitPrice, line.Quantity);
        if (quote.Coupon is { Applied: true } applied)
            order.ApplyCoupon(applied.Code, quote.Discount);

        var reservationLines = quote.Lines.Select(l => new ReservationLine(l.VariantId, l.Quantity, l.Name)).ToList();
        await _uow.InTransactionAsync(async () =>
        {
            order.AssignNumber(await _numbers.NextAsync(ct));
            order.Place(_clock.GetUtcNow().UtcDateTime);
            await _orders.AddAsync(order, ct);
            await _uow.SaveChangesAsync(ct);
            // استخدام الكوبون يُحجز في المعاملة نفسها (المرحلة 10): قواعده تُعاد على قراءة جديدة، فطلبان متزامنان على آخر
            // استخدام لا يأخذانه معاً — الخاسر يُرفض InvalidCoupon وتُلغى معاملته كلها.
            if (quote.Coupon is { Applied: true } redeemed)
                await _couponRedemptions.ReserveAsync(redeemed.Code, order.Id, customerId, quote.Subtotal, quote.Discount, ct);
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
            await _confirmation.CancelAsync(order, PaymentStartFailedNote, expired: false, OrderActor.System, ct);
            return Result<OrderCreatedDto>.Failure(Error.Unavailable(
                "PaymentUnavailable", "تعذّر بدء عملية الدفع حالياً. لم يُحجز أي مخزون، يُرجى المحاولة لاحقاً."));
        }

        order.SetPaymentIntent(intent.PaymentIntentId);
        // دفعة الطلب (المرحلة 11) بالحساب الذي أنشأ النيّة — تُحفظ مع ربطها بالطلب في الحفظ نفسه.
        await _orderPayments.RecordIntentAsync(order.Id, intent, order.TotalAmount, ct);
        await _uow.SaveChangesAsync(ct);

        return Result<OrderCreatedDto>.Success(new OrderCreatedDto(
            order.Id, order.OrderNumber, order.Status.ToString(),
            order.Subtotal.Amount, order.DiscountAmount?.Amount, order.TotalAmount.Amount, order.TotalAmount.Currency,
            intent.ClientSecret));
    }

    private static string? FromBook(Customer customer, int addressId) =>
        customer.Addresses.FirstOrDefault(a => a.Id == addressId)?.ToPostalAddress().ToSingleLine(Order.ShippingAddressMaxLength);

    private static Result<OrderCreatedDto> AddressNotFound() =>
        Result<OrderCreatedDto>.Failure(Error.Validation("AddressNotFound", "العنوان غير موجود في دفترك"));
}
