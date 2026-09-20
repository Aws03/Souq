using Souq.Application.Common.Tenancy;
using Souq.Domain.Platform;

namespace Souq.Application.Tests.TestDoubles;

// متجر اختبار: سياق مستأجر مضبوط كما يضبطه وسيط التحديد في الـ API — العملة قابلة للتغيير كي
// تُثبت الاختبارات أن المعالجات تأخذها من المتجر لا من ثابت.
public static class TestTenant
{
    // الوحدات صريحة منذ C1 (لم يعد الغياب يمنح كل شيء). الافتراضي هنا **كل الوحدات** عمداً: هذه
    // اللقطة تخدم عشرات الاختبارات التي لا شأن لها بالاستحقاق — متجرٌ مُنح ما يحتاجه ليُختبَر غيرُه.
    // الاختبارات التي **موضوعها** الاستحقاق تبني مجموعتها صراحةً (PricingServiceTests.StoreWithoutModules).
    public static TenantInfo Info(string currency = "JOD", int id = 1, IReadOnlySet<string>? modules = null) =>
        new(id, "test-store", "متجر اختبار", TenantStatus.Active, currency, "ar", "Asia/Amman",
            modules ?? new HashSet<string>(StoreModules.All, StringComparer.Ordinal));

    public static ITenantContext Context(string currency = "JOD", int id = 1)
    {
        var context = new TenantContext();
        context.UseTenant(Info(currency, id));
        return context;
    }
}
