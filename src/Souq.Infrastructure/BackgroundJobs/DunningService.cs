using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Notifications;
using Souq.Application.Common.Security;
using Souq.Application.Common.Tenancy;
using Souq.Application.Features.Billing;
using Souq.Domain.Interfaces;
using Souq.Domain.Platform;
using Souq.Infrastructure.Persistence;
using Souq.Infrastructure.Persistence.Outbox;
using Souq.Infrastructure.Tenancy;

namespace Souq.Infrastructure.BackgroundJobs;

// ============================================================================
// المطالبة الآلية وتصعيدُها إلى تعليق (C6، [ADR-0058](0058)).
//
// ============================================================================
// **هذا أخطرُ عملٍ خلفيّ في المنتج**: هو الوحيد الذي يُغلق متجرَ عميلٍ يدفع بلا إنسانٍ في الحلقة.
// ولذلك كلُّ ما يحيط به مبنيٌّ على أن يكون **مملّاً**:
//
//   • **القرارُ ليس هنا.** `DunningPolicy` دالّةٌ نقيّة تُختبر بجدول حالات، وهذا المنسّق ينفّذ
//     جوابها ولا يُضيف إليه شرطاً واحداً. أيُّ منطقٍ يتسرّب إلى هنا يصير منطقاً لا يُختبر إلّا
//     بقاعدةٍ وساعة.
//   • **معطّلٌ حتى يُفعّله إنسان** — والفحصُ في السياسة لا هنا، فلا يوجد موضعان يقرّران التعطيل.
//   • **بنطاق المنصّة لا بنطاق متجر**، ولهذا لا يرث `StoreSweepService`: ذاك يمرّ على المتاجر
//     واحداً واحداً، والمطالبةُ تقرأ ما استحقّ عبرها كلّها في استعلامٍ واحد. وهو ما تصفه خطّةُ
//     C6 حرفياً.
//   • **تحت عقدِ إيجار** (C4، ADR-0057): نسختان تُعلّقان المتجر نفسه تكتبان سطرَي تدقيق وتُرسلان
//     إشعارَين — والتعليقُ نفسه مُحصَّنٌ بعلامةٍ على الفاتورة، لكنّ الضجيج لا يُحصَّن.
//   • **والتذكيرُ يمرّ بصندوق الصادر لا بمزوّدٍ مباشرةً** (ADR-0034): المنسّق يحفظ ويُسجّل، ولا
//     ينتظر شبكةً وهو ممسكٌ بعقد.
// ============================================================================
internal sealed class DunningService : BackgroundService
{
    // حدُّ الدورة الواحدة: منصّةٌ أُهملت شهوراً لا تُحمّل عشرات الآلاف من الصفوف دفعةً واحدة،
    // والباقي يأتي في الدورة التالية — السلّمُ يُقاس بالأيام، فتأخّرُ دورةٍ لا يغيّر شيئاً.
    private const int BatchSize = 200;

    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(10);

    private readonly IServiceProvider _services;
    private readonly BillingSettings _settings;
    private readonly ILogger<DunningService> _logger;

    public DunningService(IServiceProvider services, BillingSettings settings, ILogger<DunningService> logger)
    {
        _services = services; _settings = settings; _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_settings.DunningSweepIntervalMinutes <= 0)
        {
            _logger.LogInformation("Dunning sweep is disabled by configuration");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_settings.DunningSweepIntervalMinutes));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await SweepAsync(stoppingToken);
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        await using var scope = _services.CreateAsyncScope();

        // نطاقُ المنصّة صراحةً: الفواتيرُ جداولُ منصّةٍ بلا مرشّح، والقراءةُ العابرة تحتاج نطاقاً
        // لا متجرَ فيه — وبلا ضبطه يرمي `AppDbContext` عند أوّل كيانٍ مملوكٍ لمتجر.
        scope.ServiceProvider.GetRequiredService<TenantContext>().UsePlatform();

        ILeaseHandle? lease;
        try
        {
            lease = await scope.ServiceProvider.GetRequiredService<IDistributedLock>()
                .TryAcquireAsync(GuardedWork.Dunning, LeaseDuration, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Dunning could not reach the lease table");
            return;
        }

        if (lease is null)
        {
            _logger.LogDebug("Dunning skipped: another instance holds the lease");
            return;
        }

        await using (lease)
        {
            try
            {
                await RunAsync(scope.ServiceProvider, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Dunning sweep failed");
            }
        }
    }

    private async Task RunAsync(IServiceProvider scoped, CancellationToken ct)
    {
        var settings = await scoped.GetRequiredService<IPlatformBillingSettingsRepository>().GetAsync(ct);

        // بلا إعدادٍ أصلاً لا مطالبة: منصّةٌ لم تُضبَط فوترتها لم تُصدر فاتورةً تُطالَب بها.
        if (settings is null || !settings.DunningEnabled) return;

        var clock = scoped.GetRequiredService<TimeProvider>();
        var invoices = scoped.GetRequiredService<IPlatformInvoiceRepository>();
        var now = clock.GetUtcNow().UtcDateTime;

        var overdue = await invoices.ListOverdueAsync(now, BatchSize, ct);
        if (overdue.Count == 0) return;

        var reminded = 0;
        var suspended = 0;

        foreach (var invoice in overdue)
        {
            var decision = DunningPolicy.Decide(invoice, settings, now);
            switch (decision.Action)
            {
                case DunningAction.Remind:
                    await RemindAsync(scoped, invoice, ct);
                    reminded++;
                    break;
                case DunningAction.Suspend:
                    if (await SuspendAsync(scoped, invoice, now, ct)) suspended++;
                    break;
                default:
                    break;
            }
        }

        if (reminded > 0 || suspended > 0)
            _logger.LogInformation(
                "Dunning sent {Reminded} reminder(s) and suspended {Suspended} store(s) across {Overdue} overdue invoice(s)",
                reminded, suspended, overdue.Count);
    }

    // ========================================================================
    // التذكير: يُسجَّل على الفاتورة وتُوضَع رسالتُه في الصندوق **في الحفظة نفسها** — فلا تذكيرٌ
    // يُرسَل بلا أن يُسجَّل، ولا يُسجَّل بلا أن يُرسَل.
    //
    // **والرسالةُ تُبنى بمتجرها صراحةً، لا من نطاق الطلب.** `INotificationOutbox.Enqueue` تقرأ
    // المتجر من السياق، وهذا المنسّق يعمل **بنطاق المنصّة** عمداً — فلو أُدرجت الرسالة من خلاله
    // لحملت «بلا متجر»، ولحاول المُرسِلُ معالجتها في نطاق المنصّة حيث لا متجر لها ولا مستلمين.
    // فالمتجر يُؤخذ من الفاتورة نفسها، وهو الموضع الوحيد الذي يعرفه يقيناً.
    // ========================================================================
    private static async Task RemindAsync(IServiceProvider scoped, PlatformInvoice invoice, CancellationToken ct)
    {
        var clock = scoped.GetRequiredService<TimeProvider>();
        var db = scoped.GetRequiredService<AppDbContext>();
        var now = clock.GetUtcNow().UtcDateTime;

        invoice.RecordReminder(now);
        db.OutboxMessages.Add(OutboxMessage.For(new InvoiceOverdueReminder(invoice.Id), invoice.TenantId, now));
        await scoped.GetRequiredService<IUnitOfWork>().SaveChangesAsync(ct);
    }

    // ========================================================================
    // التصعيد. **الفعلُ الذي يوجد كلُّ ما سبق لأجله**، وهو مبنيٌّ ليكون قابلاً للتراجع بيد
    // إنسان: التعليقُ حالةٌ يُعيدها المشغّل بضغطة (C3)، لا حذفٌ ولا أرشفة.
    //
    // ويقع في معاملةٍ واحدة مع علامة الفاتورة: تعليقٌ يُكتب بلا علامةٍ يُعاد في الدورة التالية،
    // وعلامةٌ تُكتب بلا تعليقٍ تعني متجراً يعمل والسلّمُ يظنّه معلَّقاً.
    // ========================================================================
    private async Task<bool> SuspendAsync(
        IServiceProvider scoped, PlatformInvoice invoice, DateTime now, CancellationToken ct)
    {
        var tenants = scoped.GetRequiredService<ITenantRepository>();
        var store = await tenants.GetByIdAsync(invoice.TenantId, ct);

        // متجرٌ أُرشف أو عُلِّق بالفعل: العلامةُ تُكتب كي لا يُعاد النظر فيه كلَّ دورة، ولا يُعلَّق
        // ما هو معلَّق. وهذا ليس فشلاً — هو الحالةُ المتوقّعة حين يُعلّق مشغّلٌ متجراً بيده أوّلاً.
        if (store is null || store.Status != TenantStatus.Active)
        {
            invoice.RecordEscalation(now);
            await scoped.GetRequiredService<IUnitOfWork>().SaveChangesAsync(ct);
            return false;
        }

        var uow = scoped.GetRequiredService<IUnitOfWork>();
        await uow.InTransactionAsync(async () =>
        {
            store.Suspend();
            invoice.RecordEscalation(now);
            await uow.SaveChangesAsync(ct);
        }, ct);

        // وما يجعل التعليق حقيقياً لا حالةً في صفّ (C3): إبطالُ جلسات المتجر، وإسقاطُ لقطته من
        // ذاكرة كلّ نسخة. بعد الالتزام لا داخله — كلاهما عملٌ خارج المعاملة (ADR-0021).
        await scoped.GetRequiredService<IStoreSessionRevoker>()
            .RevokeAllAsync(store.Id, "billing.suspended", ct);
        await scoped.GetRequiredService<ITenantDirectory>().InvalidateAsync(ct);

        _logger.LogWarning(
            "Dunning suspended store {TenantId} for unpaid invoice {InvoiceId}, {Days} day(s) overdue",
            store.Id, invoice.Id, invoice.DaysOverdueAt(now));
        return true;
    }
}
