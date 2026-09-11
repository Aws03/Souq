namespace Souq.Application.Common.Tenancy;

// ============================================================================
// TenantContext — حامل حالة المستأجر لنطاق خدمات واحد (طلب HTTP أو تكرار مهمة خلفية). صنف ملموس
// بلا تقنية (لا واجهة ثانية بلا بديل): حالات الاستخدام ترى ITenantContext للقراءة فقط، ووحدهم
// من يحدّدون المستأجر (الوسيط، المهام الخلفية، البذر) يطلبون هذا الصنف لضبطه.
// يُضبط مرة واحدة: تبديل المتجر في منتصف طلب طريق مختصر لعبور المستأجرين — العمل في متجر آخر
// يعني نطاق خدمات جديداً (TenantScopes.RunAsync في Infrastructure).
// ============================================================================
public sealed class TenantContext : ITenantContext
{
    public TenantScope Scope { get; private set; }
    public TenantInfo? Tenant { get; private set; }

    public void UseTenant(TenantInfo tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        EnsureUnset();
        Scope = TenantScope.Tenant;
        Tenant = tenant;
    }

    public void UsePlatform()
    {
        EnsureUnset();
        Scope = TenantScope.Platform;
    }

    private void EnsureUnset()
    {
        if (Scope != TenantScope.None)
            throw new InvalidOperationException("سياق المستأجر يُضبط مرة واحدة لكل نطاق خدمات.");
    }
}
