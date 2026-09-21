using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Tenancy;
using Souq.Application.Features.Coupons.Contracts;
using Souq.Application.Features.Inventory.Contracts;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Orders.Checkout;

// ============================================================================
// المرحلة الثانية من الدفع (TD-13، قُسِّم في M5): **معاملة واحدة تكتب كل شيء أو لا شيء** (المرحلة 6، ADR-0026).
//
// رقم الطلب من عدّاد المتجر (المرحلة 9)، التثبيت (تجميد الأسطر والإجماليات)، حفظ الطلب، حجز الكوبون، ثم حجز
// المخزون بمرجع الطلب ("order:{id}").
//
// **لماذا الكلّ في معاملة واحدة:** نقص حقيقي بعد قراءة المتاح (سباق) يرمي InsufficientStock من الحجز، فتُلغى
// المعاملة كلها — لا طلب بلا حجز، ولا حجز بلا طلب، ولا رقم طلب مستهلك لطلب لم يوجد. والكوبون يُحجز هنا بالذات
// لا قبلها: قواعده تُعاد على قراءة جديدة داخل المعاملة، فطلبان متزامنان على آخر استخدام لا يأخذانه معاً —
// الخاسر يُرفض InvalidCoupon وتسقط معاملته كلها معه.
//
// المحجوز ليس بيعاً: لا يُسجَّل بيعاً إلا عند تأكيد الدفع.
// ============================================================================
public sealed class OrderPlacement
{
    private readonly IOrderRepository _orders;
    private readonly IOrderNumbers _numbers;
    private readonly ICouponRedemptions _couponRedemptions;
    private readonly IInventoryReservations _reservations;
    private readonly ITenantContext _tenant;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public OrderPlacement(
        IOrderRepository orders, IOrderNumbers numbers, ICouponRedemptions couponRedemptions,
        IInventoryReservations reservations, ITenantContext tenant, IUnitOfWork uow, TimeProvider clock)
    {
        _orders = orders; _numbers = numbers; _couponRedemptions = couponRedemptions;
        _reservations = reservations; _tenant = tenant; _uow = uow; _clock = clock;
    }

    public async Task<Order> PlaceAsync(CheckoutDraft draft, CancellationToken ct)
    {
        var store = _tenant.RequireTenant();
        var quote = draft.Quote;

        // الطلب بعملة المتجر ولقطات أسطر التسعير (الاسم بلغة المتجر الافتراضية، والمتغيّر المشترى بعينه مع SKU —
        // الفاتورة تبقى كما كانت لحظة الشراء). سطر لكل متغيّر: مقاسان من المنتج نفسه سطران.
        var order = new Order(draft.CustomerId, draft.ShippingAddress, store.Currency, draft.BillingAddress);
        foreach (var line in quote.Lines)
            order.AddItem(line.ProductId, line.VariantId, line.Name, line.UnitPrice, line.Quantity,
                line.VariantLabel, line.Sku, line.UnitCost);
        if (quote.Coupon is { Applied: true } applied)
            order.ApplyCoupon(applied.Code, quote.Discount);
        if (quote.ShippingOutcome?.Selected is { } method)
            order.ApplyShipping(method.Name, method.Cost, method.Carrier, method.TrackingUrlTemplate, method.MinDays, method.MaxDays,
                draft.ShippingCountry);

        var reservationLines = quote.Lines.Select(l => new ReservationLine(l.VariantId, l.Quantity, l.Name)).ToList();
        await _uow.InTransactionAsync(async () =>
        {
            order.AssignNumber(await _numbers.NextAsync(ct));
            order.Place(_clock.GetUtcNow().UtcDateTime);
            await _orders.AddAsync(order, ct);
            await _uow.SaveChangesAsync(ct);
            if (quote.Coupon is { Applied: true } redeemed)
                await _couponRedemptions.ReserveAsync(redeemed.Code, order.Id, draft.CustomerId, quote.Subtotal, quote.Discount, ct);
            await _reservations.ReserveAsync(OrderStockReference.For(order.Id), reservationLines, ct);
        }, ct);

        return order;
    }
}
