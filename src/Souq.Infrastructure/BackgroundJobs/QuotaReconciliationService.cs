using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Souq.Application.Features.Billing;
using Souq.Application.Features.Billing.Contracts;

namespace Souq.Infrastructure.BackgroundJobs;

// ============================================================================
// مصالحة عدّادات الحصص (C2، ADR-0049 §الالتزام الثاني) — الالتزام الذي يأتي مع اختيار العدّاد:
// العدّاد صورةٌ عن الحقيقة، ومن لا يُصالحها يكتشف انحرافها حين يسدّ متجراً يدفع.
//
// ينحرف من أين؟ من كل ما يُنقِص المعدود بلا نداء ReleaseAsync — مسارٌ جديد نسيه، محوُ بياناتٍ
// لعميل، تصحيحٌ يدوي في القاعدة، وأرشفةٌ جماعية يوم تُبنى. ومن الزيادة أيضاً: صفوفٌ كُتبت قبل أن
// يوجد هذا الحارس أصلاً.
//
// **بلا MediatR خلافاً لبقيّة المنسّقات، وعن قصد.** كلّ المنطق في المنفذ نفسه (لا حالة استخدام
// تُنسّق شيئاً)، وBilling **منطقة منصّة** فكل طلب فيها يجب أن يكون مُدقَّقاً — فأمرٌ هنا كان
// يكتب سطر تدقيق لكل متجر في كل دورة إلى الأبد. سجلّ المصالحة يكفي، وهو لا يُكتب إلّا حين
// يُصحَّح شيء فعلاً.
// ============================================================================
internal sealed class QuotaReconciliationService : StoreSweepService
{
    private readonly BillingSettings _settings;
    private readonly ILogger<QuotaReconciliationService> _logger;

    public QuotaReconciliationService(
        IServiceProvider services, BillingSettings settings, ILogger<QuotaReconciliationService> logger)
        : base(services, logger)
    {
        _settings = settings; _logger = logger;
    }

    protected override string Name => "Quota reconciliation";

    protected override TimeSpan? Interval =>
        _settings.QuotaReconcileIntervalMinutes > 0
            ? TimeSpan.FromMinutes(_settings.QuotaReconcileIntervalMinutes)
            : null;

    protected override async Task<int> RunForStoreAsync(IServiceProvider scoped, CancellationToken ct)
    {
        var corrected = await scoped.GetRequiredService<ITenantQuotaGuard>().ReconcileAsync(ct);

        // الانحراف ليس روتيناً: صفرٌ هو الحالة الصحّية، وأيّ تصحيح يعني أن مساراً لم يُبلِّغ. يُقال
        // بصوتٍ مسموع كي يُلاحَق سببه، لا ليُصحَّح كل ليلة إلى الأبد بهدوء.
        if (corrected > 0)
            _logger.LogWarning(
                "Quota reconciliation corrected {Corrected} counter(s) for this store — a create or release path is not reporting",
                corrected);

        return corrected;
    }
}
