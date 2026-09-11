using Souq.Application.Common.Exceptions;

namespace Souq.Application.Common.Tenancy;

public enum TenantScope
{
    None,       // لم يُحدَّد (قبل وسيط التحديد، أو مهمة خلفية لم تضبطه): أي وصول لبيانات متجر يرمي
    Tenant,     // طلب على مضيف متجر: كل استعلام مُرشَّح بهذا المتجر، وكل كتابة مختومة به
    Platform,   // طلب على مضيف المنصّة: لا متجر؛ بيانات المتاجر عبر مسار المنصّة المُدقَّق فقط
}

// ============================================================================
// ITenantContext — منفذ "لأي متجر يُنفَّذ هذا الطلب؟" (MultiTenancy.md §3, ADR-0006). يضبطه
// الخادم وحده: وسيط تحديد المستأجر من المضيف (API)، أو المهمة الخلفية لكل متجر (Infrastructure).
// لا حالة استخدام تقرأ TenantId من أمر أو استعلام — اختبار معماري يمنع ذلك.
// ============================================================================
public interface ITenantContext
{
    TenantScope Scope { get; }

    // null إلا حين Scope == Tenant.
    TenantInfo? Tenant { get; }
}

public static class TenantContextExtensions
{
    // لحالات استخدام لا معنى لها خارج متجر (عملة المنتج، رقم الطلب). غيابه خطأ برمجي ⇒ 500 صاخب.
    public static TenantInfo RequireTenant(this ITenantContext context) =>
        context.Tenant ?? throw new TenantContextMissingException();
}
