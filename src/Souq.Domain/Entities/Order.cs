using Souq.Domain.Common;
using Souq.Domain.Enums;
using Souq.Domain.Exceptions;
using Souq.Domain.ValueObjects;

namespace Souq.Domain.Entities;

// ============================================================================
// Order — جذر التجمّع (Aggregate Root). هذا أهم نمط في طبقة الـ Domain.
//
// الفكرة: الطلب وأسطره (OrderItems) يُعامَلان كوحدة واحدة لا تتجزأ. لا أحد
// يستطيع تعديل سطر طلب من الخارج مباشرة — كل التعديلات تمرّ عبر الطلب نفسه.
//
// لماذا؟ لنحمي قاعدة لا يجوز كسرها أبداً:
//     "إجمالي الطلب = مجموع أسطره دائماً".
// لو سمحنا بتعديل الأسطر من الخارج، قد ينسى أحدهم تحديث الإجمالي → بيانات فاسدة.
// بجعل القائمة للقراءة فقط (IReadOnlyCollection) والإضافة عبر دالة واحدة،
// نجعل الحالة الفاسدة مستحيلة هندسياً، لا مجرد "ممنوعة بالاتفاق".
// ============================================================================
public class Order : Entity
{
    private readonly List<OrderItem> _items = new();
    // سجلّ انتقالات الحالة — جزء من التجمّع مثل _items تماماً. RecordStatusChange
    // هو الباب الوحيد للإضافة إليه، يُستدعى داخلياً من كل دالة تُغيّر Status،
    // فيستحيل هندسياً أن ينتقل الطلب لحالة جديدة دون أن يُسجَّل ذلك.
    private readonly List<OrderStatusHistory> _statusHistory = new();

    public int CustomerId { get; private set; }
    public OrderStatus Status { get; private set; }
    public string ShippingAddress { get; private set; } = default!;
    public string? CouponCode { get; private set; }
    public Money? DiscountAmount { get; private set; }
    public string? PaymentIntentId { get; private set; }
    public string? TrackingNumber { get; private set; }
    public string? ShippingCarrier { get; private set; }

    // نكشف الأسطر للقراءة فقط — لا يستطيع الخارج الإضافة/الحذف مباشرة.
    public IReadOnlyCollection<OrderItem> Items => _items.AsReadOnly();
    public IReadOnlyCollection<OrderStatusHistory> StatusHistory => _statusHistory.AsReadOnly();

    // الإجمالي الفرعي محسوب من الأسطر، فيستحيل أن يتعارض معها.
    public Money Subtotal =>
        _items.Aggregate(Money.Zero(), (sum, item) => sum.Add(item.LineTotal));

    // الإجمالي النهائي = الفرعي ناقص الخصم (إن وُجد كوبون مطبَّق).
    public Money TotalAmount => DiscountAmount is null ? Subtotal : Subtotal.Subtract(DiscountAmount);

    private Order() { }

    public Order(int customerId, string shippingAddress)
    {
        CustomerId = customerId;
        ShippingAddress = shippingAddress;
        Status = OrderStatus.Pending;   // كل طلب يبدأ "بانتظار الدفع"
        RecordStatusChange(null);       // سطر تاريخ أول يوثّق لحظة إنشاء الطلب
    }

    // الباب الوحيد لتسجيل سطر تاريخ — private لا internal حتى: لا يُستدعى إلا
    // من داخل Order نفسه، مباشرة بعد كل تغيير فعلي لـ Status.
    private void RecordStatusChange(string? note) => _statusHistory.Add(new OrderStatusHistory(Status, note));

    // الباب الوحيد لإضافة منتج للطلب. القاعدة محمية: لا إضافة بعد بدء المعالجة.
    public void AddItem(int productId, string productName, Money unitPrice, int quantity)
    {
        if (Status != OrderStatus.Pending)
            throw new InvalidOrderOperationException("لا يمكن تعديل طلب بدأت معالجته");
        if (quantity <= 0)
            throw new InvalidOrderOperationException("الكمية يجب أن تكون أكبر من صفر");

        // إن كان المنتج موجوداً مسبقاً، نزيد كميته بدل تكرار السطر (قاعدة من معايير القبول).
        var existing = _items.FirstOrDefault(i => i.ProductId == productId);
        if (existing is not null)
            existing.IncreaseQuantity(quantity);
        else
            _items.Add(new OrderItem(productId, productName, unitPrice, quantity));
    }

    // تطبيق كوبون خصم — قبل الدفع فقط. القيمة تصل جاهزة (Coupon.CalculateDiscount
    // في Application حسبها فعلاً)؛ هنا نحرس فقط أن الحالة والعملة والحد منطقيان،
    // كي يستحيل تطبيق خصم فاسد حتى لو أخطأ المستدعي.
    public void ApplyCoupon(string code, Money discountAmount)
    {
        if (Status != OrderStatus.Pending)
            throw new InvalidOrderOperationException("لا يمكن تطبيق كوبون على طلب بدأت معالجته");
        if (discountAmount.Currency != Subtotal.Currency)
            throw new InvalidOrderOperationException("عملة الخصم لا تطابق عملة الطلب");
        if (discountAmount.Amount > Subtotal.Amount)
            throw new InvalidOrderOperationException("قيمة الخصم أكبر من إجمالي الطلب");

        CouponCode = code;
        DiscountAmount = discountAmount;
    }

    // ربط الطلب بنيّة دفع لدى بوّابة الدفع (Stripe PaymentIntent) — قبل الدفع فقط.
    public void SetPaymentIntent(string paymentIntentId)
    {
        if (Status != OrderStatus.Pending)
            throw new InvalidOrderOperationException("لا يمكن ربط نيّة دفع بطلب بدأت معالجته");
        if (string.IsNullOrWhiteSpace(paymentIntentId))
            throw new InvalidOrderOperationException("معرّف نيّة الدفع مطلوب");
        PaymentIntentId = paymentIntentId;
    }

    // انتقالات الحالة (State Machine). كل انتقال محروس: لا يمكن شحن طلب لم يُدفع.
    // note اختياري في كل انتقال — يوثّق سبب/تفصيل الانتقال في سجلّ التاريخ
    // (مثال: سبب الإلغاء، أو ملاحظة الإدارة عند الشحن).
    public void MarkAsPaid(string? note = null)
    {
        if (Status != OrderStatus.Pending)
            throw new InvalidOrderOperationException("لا يمكن دفع طلب ليس بانتظار الدفع");
        if (!_items.Any())
            throw new InvalidOrderOperationException("لا يمكن دفع طلب فارغ");
        Status = OrderStatus.Paid;
        RecordStatusChange(note);
    }

    // trackingNumber/shippingCarrier اختياريان أيضاً — قد تُشحن الشحنة قبل توفّر
    // رقم التتبّع من شركة الشحن. يُقبلان الآن فقط (لحظة الشحن) لا لاحقاً بشكل
    // مستقلّ، تماشياً مع سير عمل الإدارة الفعلي (تُدخلان معاً عند الشحن).
    public void MarkAsShipped(string? trackingNumber = null, string? shippingCarrier = null, string? note = null)
    {
        if (Status != OrderStatus.Paid)
            throw new InvalidOrderOperationException("لا يمكن شحن طلب لم يُدفع");
        Status = OrderStatus.Shipped;
        TrackingNumber = string.IsNullOrWhiteSpace(trackingNumber) ? null : trackingNumber;
        ShippingCarrier = string.IsNullOrWhiteSpace(shippingCarrier) ? null : shippingCarrier;
        RecordStatusChange(note);
    }

    public void MarkAsDelivered(string? note = null)
    {
        if (Status != OrderStatus.Shipped)
            throw new InvalidOrderOperationException("لا يمكن تسليم طلب لم يُشحن");
        Status = OrderStatus.Delivered;
        RecordStatusChange(note);
    }

    // الإلغاء مسموح فقط من Pending/Paid — وهما بالضبط الحالتان اللتان يحجز فيهما
    // الطلب مخزوناً لم يُشحن بعد. لذا كل إلغاء ناجح يعني "حرّر المخزون مرة واحدة":
    // رفض Cancelled ⇒ Cancelled يمنع إعادة المخزون مرتين (تضخيم وهمي للمخزون).
    public void Cancel(string? note = null)
    {
        if (Status is OrderStatus.Shipped or OrderStatus.Delivered)
            throw new InvalidOrderOperationException("لا يمكن إلغاء طلب تم شحنه أو تسليمه");
        if (Status == OrderStatus.Cancelled)
            throw new InvalidOrderOperationException("الطلب ملغى مسبقاً");
        Status = OrderStatus.Cancelled;
        RecordStatusChange(note);
    }
}
