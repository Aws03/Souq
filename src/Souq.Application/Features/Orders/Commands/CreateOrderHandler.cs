using MediatR;
using Microsoft.Extensions.Logging;
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Models;
using Souq.Application.Common.Security;
using Souq.Application.Common.Tenancy;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Interfaces;
using Souq.Domain.Platform;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Features.Orders.Commands;

// ============================================================================
// CreateOrderHandler — "منسّق" حالة الاستخدام الأهم في النظام.
//
// خطّتان متعاقبتان مقصودتان على أسطر الطلب: الأولى تتحقّق فقط (وجود المنتج، توفّر
// الكمية، صلاحية الكوبون) دون أي تعديل؛ الثانية تُنقص المخزون فعلياً وتبني الطلب.
// الفائدة: أي فشل تحقّق لا يترك أثراً جزئياً على المخزون.
//
// الدفع خطوتان: هذا المعالج ينشئ الطلب Pending ويحفظه أولاً، ثم ينشئ نيّة دفع
// (بطاقة العميل لا تصل خادمنا — Stripe.js)، والتأكيد في ConfirmOrderPaymentHandler.
//
// التزامن (ADR-0013): منتجان يُشتريان معاً بنفس اللحظة يتعارضان عند الحفظ عبر
// rowversion، فيُرفض الثاني بـ 409 بدل بيع ما لا يوجد (Phase 0 C1).
// ============================================================================
public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, Result<OrderCreatedDto>>
{
    private const string PaymentStartFailedNote = "تعذّر بدء عملية الدفع";

    private readonly IProductRepository _products;
    private readonly IOrderRepository _orders;
    private readonly ICustomerRepository _customers;
    private readonly ICouponRepository _coupons;
    private readonly IStockMovementRepository _stockMovements;
    private readonly IPaymentService _payment;
    private readonly OrderStockRelease _stockRelease;
    private readonly ICurrentUser _currentUser;
    private readonly ITenantContext _tenant;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;
    private readonly ILogger<CreateOrderHandler> _logger;

    public CreateOrderHandler(
        IProductRepository products, IOrderRepository orders, ICustomerRepository customers,
        ICouponRepository coupons, IStockMovementRepository stockMovements,
        IPaymentService payment, OrderStockRelease stockRelease, ICurrentUser currentUser,
        ITenantContext tenant, IUnitOfWork uow, TimeProvider clock, ILogger<CreateOrderHandler> logger)
    {
        _products = products; _orders = orders; _customers = customers;
        _coupons = coupons; _stockMovements = stockMovements; _payment = payment;
        _stockRelease = stockRelease; _currentUser = currentUser; _tenant = tenant;
        _uow = uow; _clock = clock; _logger = logger;
    }

    public async Task<Result<OrderCreatedDto>> Handle(CreateOrderCommand cmd, CancellationToken ct)
    {
        // العميل هو المستخدم الحالي دائماً — الأمر لا يحمل معرّف عميل يمكن التلاعب به (B7).
        var customerId = _currentUser.RequireCustomerId();
        var customer = await _customers.GetByIdAsync(customerId, ct);
        if (customer is null)
            // توكن صالح لحساب لم يعد موجوداً ⇒ الهوية نفسها لم تعد صالحة (401 ⇒ إعادة دخول).
            return Result<OrderCreatedDto>.Failure(Error.Unauthorized("CustomerNotFound", "العميل غير موجود"));

        // (1) خطّة التحقّق: نتأكّد من كل سطر ونحسب الإجمالي الفرعي دون تعديل مخزون.
        var lines = new List<(Product Product, int Quantity)>();
        foreach (var line in cmd.Items)
        {
            var product = await _products.GetByIdAsync(line.ProductId, ct);
            if (product is null)
                return Result<OrderCreatedDto>.Failure(
                    Error.Validation("ProductNotFound", $"المنتج رقم {line.ProductId} غير موجود"));
            if (!product.CanFulfill(line.Quantity))
                return Result<OrderCreatedDto>.Failure(Error.BusinessRule("InsufficientStock",
                    $"الكمية المطلوبة ({line.Quantity}) من \"{product.Name}\" غير متوفرة. المتاح: {product.StockQuantity}"));
            lines.Add((product, line.Quantity));
        }

        // الطلب بعملة المتجر (لقطة مجمّدة على الطلب نفسه).
        var currency = _tenant.RequireTenant().Currency;
        var subtotal = lines.Aggregate(Money.Zero(currency), (sum, l) => sum.Add(l.Product.Price.Multiply(l.Quantity)));

        // (2) الكوبون اختياري، ويُتحقّق منه أيضاً قبل أي تعديل على المخزون. كوبون غير قابل
        // للاستخدام ⇒ InvalidCouponException يرتفع هنا — قبل إنقاص أي مخزون وقبل أي حفظ.
        Coupon? coupon = null;
        if (!string.IsNullOrWhiteSpace(cmd.CouponCode))
        {
            // وحدة الكوبونات معطّلة لهذا المتجر (D-11): الواجهة تُخفي الحقل، والخادم يرفض على أي حال.
            if (!_tenant.RequireTenant().HasModule(StoreModules.Promotions))
                return Result<OrderCreatedDto>.Failure(Error.BusinessRule("ModuleDisabled", "الكوبونات غير مفعّلة في هذا المتجر"));
            coupon = await _coupons.GetByCodeAsync(cmd.CouponCode, ct);
            if (coupon is null)
                return Result<OrderCreatedDto>.Failure(Error.BusinessRule("CouponNotFound", "رمز الكوبون غير صحيح"));
            coupon.EnsureUsable(subtotal, _clock.GetUtcNow().UtcDateTime);
        }

        // (3) خطّة التنفيذ: كل شيء صالح الآن — ننقص المخزون فعلياً ونبني الطلب.
        var order = new Order(customerId, cmd.ShippingAddress, currency);
        foreach (var (product, quantity) in lines)
        {
            product.DecreaseStock(quantity);
            order.AddItem(product.Id, product.Name, product.Price, quantity);
            // كل بيع يُسجَّل في سجلّ حركة المخزون (كمية سالبة = نقص)، ذرّياً مع الطلب.
            await _stockMovements.AddAsync(
                StockMovement.For(product, StockMovementType.Sale, -quantity), ct);
        }
        if (coupon is not null)
            order.ApplyCoupon(coupon.Code, coupon.CalculateDiscount(subtotal));

        // (4) نحفظ الطلب Pending فوراً — قبل أي محاولة دفع.
        await _orders.AddAsync(order, ct);
        await _uow.SaveChangesAsync(ct);

        // (5) ننشئ نيّة دفع لدى البوّابة ونربطها بالطلب. إن فشلت البوّابة نفسها
        // (انقطاع، مفتاح خاطئ) نُعوّض فوراً: نلغي الطلب ونحرّر مخزونه في معاملة واحدة —
        // وإلا بقي الطلب Pending يحجز المخزون للأبد بلا أي وسيلة دفع (Phase 0 C6).
        PaymentIntentResult intent;
        try
        {
            intent = await _payment.CreateIntentAsync(order.TotalAmount, order.Id.ToString(), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "فشل إنشاء نيّة الدفع للطلب {OrderId} — أُلغي الطلب وحُرّر مخزونه", order.Id);
            order.Cancel(PaymentStartFailedNote);
            await _stockRelease.ReleaseAsync(order, PaymentStartFailedNote, ct);
            await _uow.SaveChangesAsync(ct);
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
