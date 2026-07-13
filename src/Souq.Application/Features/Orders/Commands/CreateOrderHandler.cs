using MediatR;
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Models;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Exceptions;
using Souq.Domain.Interfaces;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Features.Orders.Commands;

// ============================================================================
// CreateOrderHandler — "منسّق" حالة الاستخدام الأهم في النظام.
//
// خطّتان متعاقبتان مقصودتان على أسطر الطلب (مرحلة 6 — إضافة الكوبون فرضت هذا
// الفصل): الأولى تتحقّق فقط (وجود المنتج، توفّر الكمية) وتحسب الإجمالي الفرعي
// دون أي تعديل على المخزون؛ الثانية (بعد التأكّد من صلاحية الكوبون أيضاً) تُنقص
// المخزون فعلياً وتبني الطلب. الفائدة: فشل الكوبون لا يترك أثراً جزئياً على
// المخزون — نفس روح "افشل مبكراً بلا جانبيّ" من مرحلة 4، مطبَّقة على حقل جديد.
//
// الدفع أصبح خطوتين لا خطوة واحدة (مرحلة 6 — Stripe.js حقيقي): هذا المعالج
// ينشئ نيّة دفع فقط بعد حفظ الطلب Pending (يحافظ على مبدأ "الطلب قبل أي محاولة
// دفع" من مرحلة 4)، والتأكيد الفعلي يحدث في ConfirmOrderPaymentHandler بعد أن
// يُصادق العميل على الدفع من متصفّحه مباشرة مع Stripe (بطاقته لا تصل خادمنا).
// ============================================================================
public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, Result<OrderCreatedDto>>
{
    private readonly IProductRepository _products;
    private readonly IOrderRepository _orders;
    private readonly ICustomerRepository _customers;
    private readonly ICouponRepository _coupons;
    private readonly IStockMovementRepository _stockMovements;
    private readonly IPaymentService _payment;
    private readonly IUnitOfWork _uow;

    public CreateOrderHandler(
        IProductRepository products, IOrderRepository orders, ICustomerRepository customers,
        ICouponRepository coupons, IStockMovementRepository stockMovements,
        IPaymentService payment, IUnitOfWork uow)
    {
        _products = products; _orders = orders; _customers = customers;
        _coupons = coupons; _stockMovements = stockMovements; _payment = payment; _uow = uow;
    }

    public async Task<Result<OrderCreatedDto>> Handle(CreateOrderCommand cmd, CancellationToken ct)
    {
        var customer = await _customers.GetByIdAsync(cmd.CustomerId, ct);
        if (customer is null)
            return Result<OrderCreatedDto>.Failure("العميل غير موجود", "CustomerNotFound");

        // (1) خطّة التحقّق: نتأكّد من كل سطر ونحسب الإجمالي الفرعي دون تعديل مخزون.
        var lines = new List<(Product Product, int Quantity)>();
        foreach (var line in cmd.Items)
        {
            var product = await _products.GetByIdAsync(line.ProductId, ct);
            if (product is null)
                return Result<OrderCreatedDto>.Failure($"المنتج رقم {line.ProductId} غير موجود", "ProductNotFound");
            if (!product.CanFulfill(line.Quantity))
                return Result<OrderCreatedDto>.Failure(
                    $"الكمية المطلوبة ({line.Quantity}) من \"{product.Name}\" غير متوفرة. المتاح: {product.StockQuantity}",
                    "InsufficientStock");
            lines.Add((product, line.Quantity));
        }

        var subtotal = lines.Aggregate(Money.Zero(), (sum, l) => sum.Add(l.Product.Price.Multiply(l.Quantity)));

        // (2) الكوبون اختياري، ويُتحقّق منه أيضاً قبل أي تعديل على المخزون.
        Coupon? coupon = null;
        if (!string.IsNullOrWhiteSpace(cmd.CouponCode))
        {
            coupon = await _coupons.GetByCodeAsync(cmd.CouponCode, ct);
            if (coupon is null)
                return Result<OrderCreatedDto>.Failure("رمز الكوبون غير صحيح", "CouponNotFound");
            try { coupon.EnsureUsable(subtotal, DateTime.UtcNow); }
            catch (InvalidCouponException ex) { return Result<OrderCreatedDto>.Failure(ex.Message, "InvalidCoupon"); }
        }

        // (3) خطّة التنفيذ: كل شيء صالح الآن — ننقص المخزون فعلياً ونبني الطلب.
        var order = new Order(cmd.CustomerId, cmd.ShippingAddress);
        foreach (var (product, quantity) in lines)
        {
            product.DecreaseStock(quantity);
            order.AddItem(product.Id, product.Name, product.Price, quantity);
            _products.Update(product);
            // نسجّل كل بيع في سجلّ حركة المخزون (كمية سالبة = نقص). يُحفظ ذرّياً
            // ضمن نفس معاملة الطلب أدناه — فلا بيع دون أثر مخزون ولا العكس.
            await _stockMovements.AddAsync(
                StockMovement.For(product, StockMovementType.Sale, -quantity), ct);
        }
        if (coupon is not null)
            order.ApplyCoupon(coupon.Code, coupon.CalculateDiscount(subtotal));

        // (4) نحفظ الطلب Pending فوراً — قبل أي محاولة دفع.
        await _orders.AddAsync(order, ct);
        await _uow.SaveChangesAsync(ct);

        // (5) ننشئ نيّة دفع لدى بوّابة الدفع ونربطها بالطلب (حفظ ثانٍ بسيط، مثل
        // "الدفع بعد الحفظ" في مرحلة 4 — هنا "ربط نيّة الدفع بعد الحفظ" لنفس السبب).
        var intent = await _payment.CreateIntentAsync(order.TotalAmount, order.Id.ToString(), ct);
        order.SetPaymentIntent(intent.PaymentIntentId);
        _orders.Update(order);
        await _uow.SaveChangesAsync(ct);

        return Result<OrderCreatedDto>.Success(new OrderCreatedDto(
            order.Id, order.Status.ToString(),
            order.Subtotal.Amount, order.DiscountAmount?.Amount, order.TotalAmount.Amount, order.TotalAmount.Currency,
            intent.ClientSecret));
    }
}
