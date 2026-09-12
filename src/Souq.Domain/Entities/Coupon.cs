using Souq.Domain.Common;
using Souq.Domain.Enums;
using Souq.Domain.Exceptions;
using Souq.Domain.ValueObjects;

namespace Souq.Domain.Entities;

// ============================================================================
// Coupon — كوبون خصم. الكيان يحرس قواعده الخاصة (نسبة منطقية، نافذة زمنية، حدود الاستخدام، خصم لا يتجاوز الطلب) لكنه
// لا يعرف شيئاً عن Order — التطبيق الفعلي على طلب يمرّ عبر Order.ApplyCoupon، فالقاعدة "لا خصم يتجاوز الإجمالي" محروسة
// في مكانين مستقلّين (دفاع في العمق)، لا لأن أحدهما لا يكفي، بل لأن كل كيان يحمي حدوده الخاصة.
//
// الاستخدام (المرحلة 10، ADR-0030): UsedCount يعدّ الاستخدامات المحجوزة لطلبات قائمة والمؤكَّدة بالدفع. يُؤخذ عند إنشاء
// الطلب (Redeem، داخل معاملته — تعارض rowversion يُعاد من قراءة جديدة) ويُعاد عند إلغائه (ReleaseUse)، كالمخزون تماماً:
// لا يتجاوز كوبون حدّه بطلبين متزامنين، ولا يستهلكه طلب لم يكتمل.
// ============================================================================
public class Coupon : Entity, ITenantOwned
{
    public int TenantId { get; private set; }
    public string Code { get; private set; } = default!;
    public DiscountType Type { get; private set; }
    public decimal Value { get; private set; }              // نسبة 0-100 أو مبلغ ثابت حسب Type
    public Money? MinOrderAmount { get; private set; }
    public DateTime? StartsAt { get; private set; }
    public DateTime? ExpiresAt { get; private set; }
    public int? MaxUses { get; private set; }
    public int? MaxUsesPerCustomer { get; private set; }
    public int UsedCount { get; private set; }
    public bool IsActive { get; private set; }

    private Coupon() { }

    public Coupon(string code, DiscountType type, decimal value, Money? minOrderAmount, DateTime? expiresAt, int? maxUses,
                  DateTime? startsAt = null, int? maxUsesPerCustomer = null)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new InvalidCouponException("رمز الكوبون مطلوب");

        Validate(type, value, startsAt, expiresAt, maxUses, maxUsesPerCustomer);
        Code = code.Trim().ToUpperInvariant();
        Apply(type, value, minOrderAmount, startsAt, expiresAt, maxUses, maxUsesPerCustomer);
        IsActive = true;
    }

    // تحديث معطيات الخصم (لا الرمز نفسه — يبقى ثابتاً بعد الإنشاء لأنه معرّف عام يتداوله العملاء؛ لتغييره فعلياً يُنشأ
    // كوبون جديد ويُعطَّل القديم). قيمة مرفوضة لا تغيّر شيئاً.
    public void UpdateDetails(DiscountType type, decimal value, Money? minOrderAmount, DateTime? expiresAt, int? maxUses,
                              DateTime? startsAt = null, int? maxUsesPerCustomer = null)
    {
        Validate(type, value, startsAt, expiresAt, maxUses, maxUsesPerCustomer);
        Apply(type, value, minOrderAmount, startsAt, expiresAt, maxUses, maxUsesPerCustomer);
    }

    // يتحقّق أن الكوبون صالح للاستخدام الآن على طلب بهذا الإجمالي الفرعي، لعميل له customerUses استخداماً فعّالاً منه.
    // يرمي استثناءً برسالة واضحة بدل bool صامت — المستدعي يعرضه للعميل.
    public void EnsureUsable(Money subtotal, DateTime now, int customerUses = 0)
    {
        if (!IsActive)
            throw new InvalidCouponException("الكوبون غير مُفعّل");
        if (StartsAt is not null && now < StartsAt)
            throw new InvalidCouponException("لم يبدأ العمل بهذا الكوبون بعد");
        if (ExpiresAt is not null && now > ExpiresAt)
            throw new InvalidCouponException("انتهت صلاحية الكوبون");
        if (MaxUses is not null && UsedCount >= MaxUses)
            throw new InvalidCouponException("استُنفد عدد مرات استخدام الكوبون");
        if (MaxUsesPerCustomer is not null && customerUses >= MaxUsesPerCustomer)
            throw new InvalidCouponException("استخدمت هذا الكوبون الحدّ المسموح لكل عميل");
        if (MinOrderAmount is not null)
        {
            // المقارنة بالمبلغ وحده كانت تتجاهل العملة (R-09): حدّ أدنى "50 JOD" يُقاس على مجموع بالدولار كأنّه 50
            // دولاراً. Money يحرس عملته في الجمع والطرح، لكن قراءة Amount مباشرةً تتجاوز ذلك الحارس — فالعملة تُفحص
            // هنا صراحةً. بعد قفل العملة على الكوبونات أيضاً لا ينبغي أن يقع هذا أصلاً؛ يبقى ليُظهر تضارب بيانات
            // بصوت عالٍ بدل جواب خاطئ صامت.
            if (subtotal.Currency != MinOrderAmount.Currency)
                throw new InvalidCouponException(
                    $"عملة حدّ الكوبون ({MinOrderAmount.Currency}) لا تطابق عملة الطلب ({subtotal.Currency})");
            if (subtotal.Amount < MinOrderAmount.Amount)
                throw new InvalidCouponException($"الحد الأدنى للطلب لاستخدام هذا الكوبون {MinOrderAmount}");
        }
    }

    // يحسب قيمة الخصم الفعلية على إجمالي فرعي معيّن، بلا تجاوز الإجمالي نفسه
    // (كوبون ثابت بـ20 على طلب بـ15 يخصم 15 لا 20 — لا مبلغ سالب أبداً).
    public Money CalculateDiscount(Money subtotal)
    {
        var raw = Type == DiscountType.Percentage
            ? subtotal.Amount * (Value / 100m)
            : Value;
        var clamped = Math.Min(raw, subtotal.Amount);
        // نسبة مئوية قد تُنتج كسوراً دون خانات العملة (15% من 12.345) — تقريب تجاري
        // في مكان واحد (ADR-0014) كي يتطابق الخصم المعروض مع المخزَّن مع المُحصَّل.
        return Money.FromCalculation(clamped, subtotal.Currency);
    }

    // يأخذ استخداماً لطلب، بعد القواعد كلها على القيم الحالية (عدّاد الكوبون واستخدامات العميل الفعّالة).
    public void Redeem(Money subtotal, DateTime now, int customerUses)
    {
        EnsureUsable(subtotal, now, customerUses);
        UsedCount++;
    }

    // يعيد استخدام طلب أُلغي. لا ينزل تحت الصفر (عدّادات قديمة لم تُسجَّل استخداماتها).
    public void ReleaseUse()
    {
        if (UsedCount > 0) UsedCount--;
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;

    private void Apply(DiscountType type, decimal value, Money? minOrderAmount, DateTime? startsAt, DateTime? expiresAt,
                       int? maxUses, int? maxUsesPerCustomer)
    {
        Type = type;
        Value = value;
        MinOrderAmount = minOrderAmount;
        StartsAt = startsAt;
        ExpiresAt = expiresAt;
        MaxUses = maxUses;
        MaxUsesPerCustomer = maxUsesPerCustomer;
    }

    private static void Validate(DiscountType type, decimal value, DateTime? startsAt, DateTime? expiresAt,
                                 int? maxUses, int? maxUsesPerCustomer)
    {
        if (type == DiscountType.Percentage && (value <= 0 || value > 100))
            throw new InvalidCouponException("نسبة الخصم يجب أن تكون بين 1 و100");
        if (type == DiscountType.FixedAmount && value <= 0)
            throw new InvalidCouponException("قيمة الخصم يجب أن تكون أكبر من صفر");
        if (maxUses is <= 0)
            throw new InvalidCouponException("عدد مرات الاستخدام يجب أن يكون أكبر من صفر");
        if (maxUsesPerCustomer is <= 0)
            throw new InvalidCouponException("حدّ استخدام العميل يجب أن يكون أكبر من صفر");
        if (maxUses is not null && maxUsesPerCustomer > maxUses)
            throw new InvalidCouponException("حدّ العميل لا يتجاوز الحدّ العام للكوبون");
        if (startsAt is not null && expiresAt is not null && startsAt >= expiresAt)
            throw new InvalidCouponException("تاريخ البدء يجب أن يسبق تاريخ الانتهاء");
    }
}
