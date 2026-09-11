using Souq.Domain.Common;
using Souq.Domain.Enums;
using Souq.Domain.Exceptions;
using Souq.Domain.ValueObjects;

namespace Souq.Domain.Entities;

// ============================================================================
// Coupon — كوبون خصم. الكيان يحرس قواعده الخاصة (نسبة منطقية، خصم لا يتجاوز
// الطلب) لكنه لا يعرف شيئاً عن Order — التطبيق الفعلي على طلب معيّن يمرّ عبر
// Order.ApplyCoupon، فالقاعدة "لا خصم يتجاوز الإجمالي" محروسة في مكانين مستقلّين
// (دفاع في العمق)، لا لأن أحدهما لا يكفي، بل لأن كل كيان يحمي حدوده الخاصة.
// ============================================================================
public class Coupon : Entity
{
    public string Code { get; private set; } = default!;
    public DiscountType Type { get; private set; }
    public decimal Value { get; private set; }              // نسبة 0-100 أو مبلغ ثابت حسب Type
    public Money? MinOrderAmount { get; private set; }
    public DateTime? ExpiresAt { get; private set; }
    public int? MaxUses { get; private set; }
    public int UsedCount { get; private set; }
    public bool IsActive { get; private set; }

    private Coupon() { }

    public Coupon(string code, DiscountType type, decimal value,
                  Money? minOrderAmount, DateTime? expiresAt, int? maxUses)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new InvalidCouponException("رمز الكوبون مطلوب");

        ValidateValue(type, value);

        Code = code.Trim().ToUpperInvariant();
        Type = type;
        Value = value;
        MinOrderAmount = minOrderAmount;
        ExpiresAt = expiresAt;
        MaxUses = maxUses;
        IsActive = true;
    }

    private static void ValidateValue(DiscountType type, decimal value)
    {
        if (type == DiscountType.Percentage && (value <= 0 || value > 100))
            throw new InvalidCouponException("نسبة الخصم يجب أن تكون بين 1 و100");
        if (type == DiscountType.FixedAmount && value <= 0)
            throw new InvalidCouponException("قيمة الخصم يجب أن تكون أكبر من صفر");
    }

    // تحديث معطيات الخصم (لا الرمز نفسه — يبقى ثابتاً بعد الإنشاء لأنه معرّف عام
    // يتداوله العملاء؛ لتغييره فعلياً يُنشأ كوبون جديد ويُعطَّل القديم).
    public void UpdateDetails(DiscountType type, decimal value, Money? minOrderAmount, DateTime? expiresAt, int? maxUses)
    {
        ValidateValue(type, value);
        Type = type;
        Value = value;
        MinOrderAmount = minOrderAmount;
        ExpiresAt = expiresAt;
        MaxUses = maxUses;
    }

    // يتحقّق أن الكوبون صالح للاستخدام الآن على طلب بهذا الإجمالي الفرعي.
    // يرمي استثناءً برسالة واضحة بدل bool صامت — المستدعي (Application) يترجمه
    // مباشرة لرسالة خطأ يعرضها للعميل.
    public void EnsureUsable(Money subtotal, DateTime now)
    {
        if (!IsActive)
            throw new InvalidCouponException("الكوبون غير مُفعّل");
        if (ExpiresAt is not null && now > ExpiresAt)
            throw new InvalidCouponException("انتهت صلاحية الكوبون");
        if (MaxUses is not null && UsedCount >= MaxUses)
            throw new InvalidCouponException("استُنفد عدد مرات استخدام الكوبون");
        if (MinOrderAmount is not null && subtotal.Amount < MinOrderAmount.Amount)
            throw new InvalidCouponException($"الحد الأدنى للطلب لاستخدام هذا الكوبون {MinOrderAmount}");
    }

    // يحسب قيمة الخصم الفعلية على إجمالي فرعي معيّن، بلا تجاوز الإجمالي نفسه
    // (كوبون "-20 د.أ" على طلب بـ15 د.أ يخصم 15 لا 20 — لا مبلغ سالب أبداً).
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

    public void IncrementUsage() => UsedCount++;
    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;
}
