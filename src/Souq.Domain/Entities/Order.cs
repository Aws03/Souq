using System.Security.Cryptography;
using Souq.Domain.Common;
using Souq.Domain.Enums;
using Souq.Domain.Events;
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
//
// المرحلة 9: رقم متسلسل داخل المتجر، رمز تتبّع عشوائي للرابط العام (بدل المعرّف — B8)، لقطة عنوان الفوترة، وتثبيت
// (Place) يجمّد الأسطر والخصم والإجماليات. كل انتقال حالة يمرّ بجدول OrderTransitions ويُسجَّل معه من فعله.
// ============================================================================
public class Order : Entity, ITenantOwned
{
    public const int ShippingAddressMaxLength = 500;
    public const int ShippingMethodMaxLength = ShippingMethod.NameMaxLength;
    public const int TrackingTokenLength = 32;   // 128 بت بالست عشري

    private readonly List<OrderItem> _items = new();
    // سجلّ انتقالات الحالة — جزء من التجمّع مثل _items تماماً. RecordStatusChange
    // هو الباب الوحيد للإضافة إليه، يُستدعى داخلياً من كل دالة تُغيّر Status،
    // فيستحيل هندسياً أن ينتقل الطلب لحالة جديدة دون أن يُسجَّل ذلك.
    private readonly List<OrderStatusHistory> _statusHistory = new();

    public int TenantId { get; private set; }
    public int CustomerId { get; private set; }

    // رقم الطلب داخل المتجر (يراه العميل والإدارة)؛ 0 حتى يُعيَّن من عدّاد المتجر قبل الحفظ الأول.
    public int OrderNumber { get; private set; }

    // رمز رابط التتبّع العام: عشوائي لا يُخمَّن ولا يُعدَّد (المعرّف التسلسلي كان يكشف كل الطلبات — B8).
    public string TrackingToken { get; private set; } = default!;

    // عملة الطلب لقطة من عملة المتجر لحظة الإنشاء: كل سطر وخصم بها، والإجمالي يُجمع بها حتى
    // لطلب فارغ (كان Money.Zero() يفترض JOD لكل المتاجر — Phase 2).
    public string Currency { get; private set; } = default!;
    public OrderStatus Status { get; private set; }
    public string ShippingAddress { get; private set; } = default!;
    public string BillingAddress { get; private set; } = default!;
    public string? CouponCode { get; private set; }
    public Money? DiscountAmount { get; private set; }
    public string? PaymentIntentId { get; private set; }
    public string? TrackingNumber { get; private set; }
    public string? ShippingCarrier { get; private set; }

    // لقطة الشحن (المرحلة 12): الطريقة المختارة وتكلفتها بعملة الطلب ومدّتها ودولة العنوان وقالب رابط تتبّع ناقلها. طلب
    // بلا طريقة (متجر لم يضبط الشحن، أو ما قبل المرحلة) تكلفته صفر.
    public string? ShippingMethodName { get; private set; }
    public decimal ShippingAmount { get; private set; }
    public int? ShippingMinDays { get; private set; }
    public int? ShippingMaxDays { get; private set; }
    public string? ShippingCountry { get; private set; }
    public string? ShippingTrackingUrlTemplate { get; private set; }

    // التثبيت (المرحلة 9): لحظته، والإجماليات كما صدرت بها الفاتورة — أعمدة تقرؤها القوائم بلا جمع.
    public DateTime? PlacedAt { get; private set; }
    public decimal PlacedSubtotal { get; private set; }
    public decimal PlacedTotal { get; private set; }

    public bool IsPlaced => PlacedAt is not null;

    // نكشف الأسطر للقراءة فقط — لا يستطيع الخارج الإضافة/الحذف مباشرة.
    public IReadOnlyCollection<OrderItem> Items => _items.AsReadOnly();
    public IReadOnlyCollection<OrderStatusHistory> StatusHistory => _statusHistory.AsReadOnly();

    // الإجمالي الفرعي محسوب من الأسطر، فيستحيل أن يتعارض معها.
    public Money Subtotal =>
        _items.Aggregate(Money.Zero(Currency), (sum, item) => sum.Add(item.LineTotal));

    public Money ShippingCost => new(ShippingAmount, Currency);

    // الإجمالي النهائي = الفرعي ناقص الخصم (إن وُجد كوبون مطبَّق) زائد الشحن (المرحلة 12).
    public Money TotalAmount => (DiscountAmount is null ? Subtotal : Subtotal.Subtract(DiscountAmount)).Add(ShippingCost);

    // رابط تتبّع الشحنة من قالب ناقلها ورقمها (يظهر بعد الشحن برقم).
    public string? TrackingUrl => ShippingMethod.TrackingUrl(ShippingTrackingUrlTemplate, TrackingNumber);

    private Order() { }

    // عنوان الفوترة لقطة مثل الشحن؛ بلا عنوان فوترة ⇒ عنوان الشحن نفسه.
    public Order(int customerId, string shippingAddress, string currency, string? billingAddress = null)
    {
        ShippingAddress = Address(shippingAddress, "عنوان الشحن");
        BillingAddress = billingAddress is null ? ShippingAddress : Address(billingAddress, "عنوان الفوترة");
        CustomerId = customerId;
        Currency = Money.Zero(currency).Currency;   // يتحقّق من الرمز ويوحّد صيغته
        TrackingToken = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(TrackingTokenLength / 2));
        Status = OrderStatus.Pending;   // كل طلب يبدأ "بانتظار الدفع"
        RecordStatusChange(null, OrderActor.Customer());   // سطر تاريخ أول يوثّق لحظة إنشاء الطلب
    }

    // الباب الوحيد لتسجيل سطر تاريخ — private لا internal حتى: لا يُستدعى إلا
    // من داخل Order نفسه، مباشرة بعد كل تغيير فعلي لـ Status.
    private void RecordStatusChange(string? note, OrderActor by) => _statusHistory.Add(new OrderStatusHistory(Status, note, by));

    // الرقم يُعيَّن مرة واحدة من عدّاد المتجر داخل معاملة الإنشاء.
    public void AssignNumber(int number)
    {
        if (OrderNumber != 0)
            throw new InvalidOrderOperationException("رقم الطلب معيَّن مسبقاً");
        if (number <= 0)
            throw new InvalidOrderOperationException("رقم الطلب غير صالح");
        OrderNumber = number;
    }

    // الباب الوحيد لإضافة منتج للطلب. القاعدة محمية: لا إضافة بعد التثبيت ولا بعد بدء المعالجة.
    public void AddItem(int productId, string productName, Money unitPrice, int quantity)
    {
        EnsureOpen("لا يمكن تعديل طلب بدأت معالجته");
        if (quantity <= 0)
            throw new InvalidOrderOperationException("الكمية يجب أن تكون أكبر من صفر");
        if (unitPrice.Currency != Currency)
            throw new InvalidOrderOperationException("عملة السعر لا تطابق عملة الطلب");

        // إن كان المنتج موجوداً مسبقاً، نزيد كميته بدل تكرار السطر (قاعدة من معايير القبول).
        var existing = _items.FirstOrDefault(i => i.ProductId == productId);
        if (existing is not null)
            existing.IncreaseQuantity(quantity);
        else
            _items.Add(new OrderItem(productId, productName, unitPrice, quantity));
    }

    // تطبيق كوبون خصم — قبل التثبيت فقط. القيمة تصل جاهزة (خطّ التسعير في Application حسبها)؛ هنا نحرس فقط أن الحالة
    // والعملة والحد منطقية، كي يستحيل تطبيق خصم فاسد حتى لو أخطأ المستدعي.
    public void ApplyCoupon(string code, Money discountAmount)
    {
        EnsureOpen("لا يمكن تطبيق كوبون على طلب بدأت معالجته");
        if (discountAmount.Currency != Subtotal.Currency)
            throw new InvalidOrderOperationException("عملة الخصم لا تطابق عملة الطلب");
        if (discountAmount.Amount > Subtotal.Amount)
            throw new InvalidOrderOperationException("قيمة الخصم أكبر من إجمالي الطلب");

        CouponCode = code;
        DiscountAmount = discountAmount;
    }

    // طريقة الشحن المختارة لقطةً (المرحلة 12) — قبل التثبيت فقط؛ التكلفة جاهزة من خطّ التسعير بعملة الطلب. ناقل الطريقة
    // يصير ناقل الشحنة الافتراضي (الشحن قد يحدّد غيره).
    public void ApplyShipping(string methodName, Money cost, string? carrier, string? trackingUrlTemplate,
                              int? minDays, int? maxDays, string? country)
    {
        EnsureOpen("لا يمكن تغيير الشحن لطلب بدأت معالجته");
        if (string.IsNullOrWhiteSpace(methodName) || methodName.Trim().Length > ShippingMethodMaxLength)
            throw new InvalidOrderOperationException("اسم طريقة الشحن مطلوب");
        if (cost.Currency != Currency)
            throw new InvalidOrderOperationException("عملة الشحن لا تطابق عملة الطلب");
        if (country is not null && (country.Length != 2 || !country.All(char.IsAsciiLetter)))
            throw new InvalidOrderOperationException("دولة الشحن برمز ISO من حرفين");

        ShippingMethodName = methodName.Trim();
        ShippingAmount = cost.Amount;
        ShippingCarrier = string.IsNullOrWhiteSpace(carrier) ? null : carrier.Trim();
        ShippingTrackingUrlTemplate = string.IsNullOrWhiteSpace(trackingUrlTemplate) ? null : trackingUrlTemplate.Trim();
        ShippingMinDays = minDays;
        ShippingMaxDays = maxDays;
        ShippingCountry = country?.ToUpperInvariant();
    }

    // تثبيت الطلب عند إنشائه: بعده لا سطر يُضاف ولا خصم يتغيّر، والإجماليات تُحفظ كما هي — الفاتورة لا تتغيّر.
    public void Place(DateTime placedAt)
    {
        if (IsPlaced)
            throw new InvalidOrderOperationException("الطلب مثبَّت مسبقاً");
        if (!_items.Any())
            throw new InvalidOrderOperationException("لا يمكن تثبيت طلب فارغ");
        if (OrderNumber == 0)
            throw new InvalidOrderOperationException("الطلب بلا رقم");

        PlacedAt = placedAt;
        PlacedSubtotal = Subtotal.Amount;
        PlacedTotal = TotalAmount.Amount;
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

    // انتقالات الحالة عبر جدول OrderTransitions. note اختياري في كل انتقال — يوثّق سبب/تفصيل الانتقال في سجلّ التاريخ
    // (سبب الإلغاء، ملاحظة الشحن). by: من فعله (افتراضياً النظام).
    public void MarkAsPaid(string? note = null, OrderActor? by = null)
    {
        if (Status == OrderStatus.Pending && !_items.Any())
            throw new InvalidOrderOperationException("لا يمكن دفع طلب فارغ");
        MoveTo(OrderStatus.Paid, by, note, "لا يمكن دفع طلب ليس بانتظار الدفع");
    }

    // trackingNumber/shippingCarrier اختياريان — قد تُشحن الشحنة قبل توفّر رقم التتبّع من شركة الشحن. يُقبلان لحظة
    // الشحن فقط، تماشياً مع سير عمل الإدارة الفعلي.
    public void MarkAsShipped(string? trackingNumber = null, string? shippingCarrier = null, string? note = null, OrderActor? by = null)
    {
        MoveTo(OrderStatus.Shipped, by, note, "لا يمكن شحن طلب لم يُدفع");
        TrackingNumber = string.IsNullOrWhiteSpace(trackingNumber) ? null : trackingNumber;
        // بلا ناقل صريح يبقى ناقل طريقة الشحن المختارة (المرحلة 12).
        ShippingCarrier = string.IsNullOrWhiteSpace(shippingCarrier) ? ShippingCarrier : shippingCarrier;
    }

    public void MarkAsDelivered(string? note = null, OrderActor? by = null) =>
        MoveTo(OrderStatus.Delivered, by, note, "لا يمكن تسليم طلب لم يُشحن");

    // الإلغاء من Pending/Paid فقط — الحالتان اللتان يحجز فيهما الطلب مخزوناً لم يُشحن. رفض Cancelled ⇒ Cancelled يمنع
    // إعادة المخزون مرتين. العميل يلغي قبل الدفع فقط.
    public void Cancel(string? note = null, OrderActor? by = null)
    {
        if (Status == OrderStatus.Cancelled)
            throw new InvalidOrderOperationException("الطلب ملغى مسبقاً");
        var refusal = by?.Kind == OrderActorKind.Customer && Status == OrderStatus.Paid
            ? "الطلب مدفوع: إلغاؤه يتمّ عبر المتجر"
            : "لا يمكن إلغاء طلب تم شحنه أو تسليمه";
        MoveTo(OrderStatus.Cancelled, by, note, refusal);
    }

    private void MoveTo(OrderStatus target, OrderActor? by, string? note, string refusal)
    {
        var actor = by ?? OrderActor.System;
        if (!OrderTransitions.CanMove(Status, target, actor))
            throw new InvalidOrderOperationException(refusal);
        var from = Status;
        Status = target;
        RecordStatusChange(note, actor);
        // المرحلة 14: بريد العميل وإشعارات الإدارة تنطلق من هذا الحدث (صندوق الصادر) — لطلب محفوظ فقط.
        if (Id > 0) Raise(new OrderStatusChanged(Id, CustomerId, from, target, actor.Kind));
    }

    private void EnsureOpen(string refusal)
    {
        if (Status != OrderStatus.Pending)
            throw new InvalidOrderOperationException(refusal);
        if (IsPlaced)
            throw new InvalidOrderOperationException("الطلب مثبَّت: أسطره وخصمه وإجمالياته لا تتغيّر");
    }

    private static string Address(string? value, string label)
    {
        var address = value?.Trim() ?? "";
        if (address.Length is 0 or > ShippingAddressMaxLength)
            throw new InvalidOrderOperationException($"{label} مطلوب (حتى {ShippingAddressMaxLength} حرف)");
        return address;
    }
}
