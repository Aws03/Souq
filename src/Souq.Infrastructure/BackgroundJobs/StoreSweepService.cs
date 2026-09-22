using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Tenancy;
using Souq.Infrastructure.Tenancy;

namespace Souq.Infrastructure.BackgroundJobs;

// ============================================================================
// أساس المنسّقات الدورية لكل متجر (D-15: خادم .NET خلفي، بلا Hangfire حتى الحاجة). كل دورة يمرّ على المتاجر النشطة والموقوفة
// ويشغّل عمل الدورة داخل نطاق كل متجر — المرشّح وحارس الكتابة يعملان كما في أي طلب HTTP. أي فشل يُسجَّل ولا يوقف
// الخادم ولا بقية المتاجر.
//
// ============================================================================
// **ومنذ C4 ([ADR-0057](0057)) تجري الدورة تحت عقدِ إيجارٍ يعبر النسخ**، فنسخةٌ واحدة تكنسُ في
// كل دورة. وما كان مكتوباً هنا قبلها — «نسختان آمنتان لكن بعمل مكرّر» — كان صحيحاً في نصفه
// وحده: العملياتُ مضمونةُ التكرار فعلاً، **لكنّ مصالحةَ عدّادات الحصص ليست كذلك في أثرها**.
// فهي تُسجّل تحذيراً كلّما صحّحت انحرافاً، وتحذيرُها يعني «مسارٌ لا يُبلغ» — ونسختان تعُدّان
// الصفوفَ نفسها في اللحظة نفسها تُنتجان ذلك التحذير بلا انحرافٍ أصلاً. أي أنّ التكرار لا يُفسد
// البيانات، لكنّه **يُفسد الإشارة** التي وُجدت لتقول إنّ شيئاً معطوب.
//
// ولا ينتظر مَن لا يحصل على العقد: ينصرف بصمت ويُحاول في الدورة التالية. عملٌ دوريّ لا يحتاج
// طابوراً — يحتاج ألّا يقع مرّتين.
//
// **والعقدُ يُجدَّد بين متجرٍ وآخر**، لا يُؤخذ مرّةً ويُنسى: دورةٌ على مئة متجر قد تتجاوز مدّتَه،
// وعقدٌ انتهى في منتصفها يعني نسخةً ثانية بدأت تكنس تحتنا. ففقدانُه يوقف الدورة في حينها.
// ============================================================================
internal abstract class StoreSweepService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger _logger;

    protected StoreSweepService(IServiceProvider services, ILogger logger)
    {
        _services = services; _logger = logger;
    }

    protected abstract string Name { get; }

    // null = المنسّق معطّل بالإعداد (الاختبارات ترسل أوامره مباشرة ولا تسابقها دورة خلفية).
    protected abstract TimeSpan? Interval { get; }

    // عمل دورة لمتجر واحد داخل نطاقه؛ يعيد عدد ما عالجه.
    protected abstract Task<int> RunForStoreAsync(IServiceProvider scoped, CancellationToken ct);

    // مدّةُ العقد. أطولُ من الفاصل بين الدورات كي لا ينتهي بين متجرٍ وآخر في الحالة العادية،
    // وقصيرةٌ بما يكفي لأن تُستعاد الكنسُ بسرعةٍ معقولة حين تسقط نسخةٌ وهي حاملةٌ له.
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);

    // ============================================================================
    // **عقدٌ لكل نوعِ كنسٍ، لا عقدٌ واحد لها جميعاً.**
    //
    // اسمٌ مشترك كان سيجعل مصالحةَ العدّادات تحجب مسحَ سجلّ البحث: كنسةٌ واحدة في المنصّة كلّها
    // في كل لحظة، وخمسُ كنساتٍ مستقلّة تتزاحم على قفلٍ لا يحرس أيّاً منها من الأخرى — فهي لا
    // تتنازع شيئاً أصلاً. ما يجب أن يُمنع هو أن تجري **الكنسةُ نفسها** في نسختين.
    //
    // والاسمُ مشتقٌّ من `Name` لا مكتوبٌ في كل صنف: اسمان متقاربان لكنسةٍ واحدة يعنيان عقدين لا
    // يحرس أيٌّ منهما شيئاً، وهو أسوأُ من ألّا يكون هناك عقد.
    // ============================================================================
    private string LeaseName => $"{GuardedWork.StoreSweeps}.{Slug(Name)}";

    private static string Slug(string name) =>
        new string(name.ToLowerInvariant().Select(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) ? c : '-').ToArray());

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (Interval is not { } interval)
        {
            _logger.LogInformation("{Sweep} is disabled by configuration", Name);
            return;
        }

        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await SweepAsync(stoppingToken);
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        // العقدُ يُطلب في نطاقٍ يعيش طولَ الدورة: `IDistributedLock` يعتمد على `AppDbContext`،
        // وهو بعمر النطاق — فإغلاقُ النطاق قبل نهاية الكنس كان سيقطع التجديد في منتصفه.
        await using var leaseScope = _services.CreateAsyncScope();
        var coordinator = leaseScope.ServiceProvider.GetRequiredService<IDistributedLock>();

        ILeaseHandle? lease;
        try
        {
            lease = await coordinator.TryAcquireAsync(LeaseName, LeaseDuration, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "{Sweep} could not reach the lease table", Name);
            return;
        }

        if (lease is null)
        {
            // نسخةٌ أخرى تكنس الآن. `Debug` لا `Information`: هذا هو الوضع **العاديّ** على أيّ
            // نشرٍ بأكثر من نسخة، وتسجيلُه على مستوى أعلى كان سيملأ السجلّ بلا حدث.
            _logger.LogDebug("{Sweep} skipped: another instance holds the lease", Name);
            return;
        }

        await using (lease)
        {
            IReadOnlyList<TenantInfo> stores;
            try
            {
                await using var scope = _services.CreateAsyncScope();
                stores = await scope.ServiceProvider.GetRequiredService<ITenantDirectory>().ListForBackgroundSweepsAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "{Sweep} could not list stores", Name);
                return;
            }

            foreach (var store in stores)
            {
                // **التجديد قبل كل متجر، والتوقّف إن فُقد.** جوابُ `RenewAsync` ليس زينة: عقدٌ
                // انتهى وأخذته نسخةٌ أخرى يعني أنّ المضيَّ قُدُماً هو بالضبط الكنسُ المزدوج الذي
                // وُجد القفلُ لمنعه.
                if (!await lease.RenewAsync(LeaseDuration, ct))
                {
                    _logger.LogWarning("{Sweep} lost its lease mid-sweep and stopped", Name);
                    return;
                }

                try
                {
                    var processed = await TenantScopes.RunAsync(_services, store, provider => RunForStoreAsync(provider, ct));
                    if (processed > 0)
                        _logger.LogInformation("{Sweep} processed {Count} items for store {TenantId}", Name, processed, store.Id);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "{Sweep} failed for store {TenantId}", Name, store.Id);
                }
            }
        }
    }
}
