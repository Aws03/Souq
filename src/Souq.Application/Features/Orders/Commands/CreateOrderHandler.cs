using MediatR;
using Microsoft.Extensions.Logging;
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Models;
using Souq.Application.Common.Security;
using Souq.Application.Common.Tenancy;
using Souq.Application.Features.Inventory.Contracts;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;
using Souq.Domain.Platform;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Features.Orders.Commands;

// ============================================================================
// CreateOrderHandler — "منسّق" حالة الاستخدام الأهم في النظام.
//
// (1) تحقّق بلا أثر: المنتجات قابلة للبيع، المتاح يكفي (قراءة مبكرة لرسالة واضحة)، والكوبون صالح.
// (2) الطلب Pending والحجز في معاملة واحدة (المرحلة 6، ADR-0026): حفظ الطلب يولّد معرّفه، ثم يحجز Inventory
//     الأسطر بمرجعه ("order:{id}"). نقص حقيقي بعد القراءة (سباق) ⇒ InsufficientStock وتُلغى المعاملة كلها — لا
//     طلب بلا حجز ولا حجز بلا طلب. المخزون المحجوز لا يُسجَّل بيعاً إلا عند الدفع.
// (3) نيّة الدفع خارج أي معاملة (ADR-0021)؛ فشل البوّابة ⇒ تعويض فوري: إلغاء الطلب وتحرير الحجز (Phase 0 C6).
//     طلب هُجر بعد ذلك يلتقطه منسّق انتهاء المهلة (ExpireStaleCheckouts).
// ============================================================================
public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, Result<OrderCreatedDto>>
{
    private const string PaymentStartFailedNote = "تعذّر بدء عملية الدفع";

    private readonly IProductRepository _products;
    private readonly IOrderRepository _orders;
    private readonly ICustomerRepository _customers;
    private readonly ICouponRepository _coupons;
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
        IProductRepository products, IOrderRepository orders, ICustomerRepository customers, ICouponRepository coupons,
        IInventoryReservations reservations, IStockAvailability availability, IPaymentService payment,
        OrderPaymentConfirmation confirmation, ICurrentUser currentUser, ITenantContext tenant, IUnitOfWork uow,
        TimeProvider clock, ILogger<CreateOrderHandler> logger)
    {
        _products = products; _orders = orders; _customers = customers; _coupons = coupons;
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

        var store = _tenant.RequireTenant();

        // (1) التحقّق: كل سطر لمنتج قابل للبيع في هذا المتجر (المستودع مُرشَّح بالمتجر).
        var lines = new List<(Product Product, int Quantity)>();
        foreach (var line in cmd.Items)
        {
            var product = await _products.GetByIdAsync(line.ProductId, ct);
            if (product is null || !product.IsActive)
                return Result<OrderCreatedDto>.Failure(
                    Error.Validation("ProductNotFound", $"المنتج رقم {line.ProductId} غير متاح"));
            lines.Add((product, line.Quantity));
        }

        var available = await _availability.AvailableAsync(lines.Select(l => l.Product.DefaultVariant.Id).Distinct().ToList(), ct);
        foreach (var group in lines.GroupBy(l => l.Product.DefaultVariant.Id))
        {
            var requested = group.Sum(l => l.Quantity);
            var inStock = available.GetValueOrDefault(group.Key);
            if (requested > inStock)
                return Result<OrderCreatedDto>.Failure(Error.BusinessRule("InsufficientStock",
                    $"الكمية المطلوبة ({requested}) من \"{group.First().Product.NameIn(store.DefaultCulture)}\" غير متوفرة. المتاح: {Math.Max(inStock, 0)}"));
        }

        // الطلب بعملة المتجر (لقطة مجمّدة على الطلب نفسه).
        var subtotal = lines.Aggregate(Money.Zero(store.Currency), (sum, l) => sum.Add(l.Product.Price.Multiply(l.Quantity)));

        // الكوبون اختياري، ويُتحقّق منه قبل أي كتابة. غير قابل للاستخدام ⇒ InvalidCouponException (422) هنا.
        Coupon? coupon = null;
        if (!string.IsNullOrWhiteSpace(cmd.CouponCode))
        {
            // وحدة الكوبونات معطّلة لهذا المتجر (D-11): الواجهة تُخفي الحقل، والخادم يرفض على أي حال.
            if (!store.HasModule(StoreModules.Promotions))
                return Result<OrderCreatedDto>.Failure(Error.BusinessRule("ModuleDisabled", "الكوبونات غير مفعّلة في هذا المتجر"));
            coupon = await _coupons.GetByCodeAsync(cmd.CouponCode, ct);
            if (coupon is null)
                return Result<OrderCreatedDto>.Failure(Error.BusinessRule("CouponNotFound", "رمز الكوبون غير صحيح"));
            coupon.EnsureUsable(subtotal, _clock.GetUtcNow().UtcDateTime);
        }

        // (2) الطلب بلقطات الأسطر (الاسم بلغة المتجر الافتراضية — الفاتورة تبقى كما كانت لحظة الشراء)، ثم الحجز.
        var order = new Order(customerId, cmd.ShippingAddress, store.Currency);
        foreach (var (product, quantity) in lines)
            order.AddItem(product.Id, product.NameIn(store.DefaultCulture), product.Price, quantity);
        if (coupon is not null)
            order.ApplyCoupon(coupon.Code, coupon.CalculateDiscount(subtotal));

        var reservationLines = lines
            .Select(l => new ReservationLine(l.Product.DefaultVariant.Id, l.Quantity, l.Product.NameIn(store.DefaultCulture)))
            .ToList();
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
