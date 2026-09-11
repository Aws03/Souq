using Souq.Application.Common.Tenancy;
using Souq.Domain.Platform;

namespace Souq.Application.Tests.TestDoubles;

// متجر اختبار: سياق مستأجر مضبوط كما يضبطه وسيط التحديد في الـ API — العملة قابلة للتغيير كي
// تُثبت الاختبارات أن المعالجات تأخذها من المتجر لا من ثابت.
public static class TestTenant
{
    public static TenantInfo Info(string currency = "JOD", int id = 1) =>
        new(id, "test-store", "متجر اختبار", TenantStatus.Active, currency, "ar", "Asia/Amman");

    public static ITenantContext Context(string currency = "JOD", int id = 1)
    {
        var context = new TenantContext();
        context.UseTenant(Info(currency, id));
        return context;
    }
}
