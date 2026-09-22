using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Souq.Application.Common.Interfaces;
using Souq.Infrastructure.Coordination;
using Souq.Infrastructure.Persistence;
using Souq.IntegrationTests.Infrastructure;

namespace Souq.IntegrationTests;

// ============================================================================
// القفلُ الذي يعبر نسخَ الخادم، على SQL Server حقيقيّ (C4، [ADR-0057](0057)).
//
// **ولا يُختبر هذا بمُقلَّد إطلاقاً.** ما يُقاس هنا ليس منطقَ صنفٍ بل سلوكُ **تحديثٍ مشروطٍ تحت
// التزامن**: هل تمنع القاعدةُ فعلاً نسختين من حمل العقد نفسه؟ مُقلَّدٌ في الذاكرة كان سيُجيب
// «نعم» مهما كانت الجملة مكتوبة — وهو بالضبط الاختبارُ الأجوف الذي يُطمئن ولا يحرس.
//
// وكلُّ اختبارٍ هنا يستعمل اسمَ عملٍ **فريداً لتشغيله**: الجدولُ عالميّ تتشاركه كلُّ الاختبارات،
// واسمٌ ثابت كان سيجعل ترتيبَ التشغيل يقرّر النتيجة.
// ============================================================================
[Collection(IntegrationCollection.Name)]
public class DistributedLeaseTests
{
    private readonly SouqApiFactory _factory;

    public DistributedLeaseTests(SouqApiFactory factory) => _factory = factory;

    private static string Work() => $"sweeps.store.qa-{Guid.NewGuid():N}"[..40];

    // نسخةُ خادمٍ مستقلّة: نطاقُ خدماتٍ خاصّ بها **وهويّةٌ خاصّة بها**. الهويّةُ مفردةٌ في
    // التطبيق الحقيقيّ (نسخةٌ واحدة لكل عملية)، فمحاكاةُ نسختين تعني هويّتين.
    private async Task<(AsyncServiceScope Scope, IDistributedLock Lock)> InstanceAsync()
    {
        var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        var logger = scope.ServiceProvider
            .GetRequiredService<Microsoft.Extensions.Logging.ILogger<SqlDistributedLock>>();
        return (scope, new SqlDistributedLock(db, clock, new InstanceIdentity(), logger));
    }

    // ========================================================================
    // **الثابتُ الذي يوجد القفلُ لأجله**: نسختان تطلبان العملَ نفسه، واحدةٌ تفوز.
    //
    // ولو انقلب هذا يوماً لَجرت كنسةُ مصالحةِ العدّادات في نسختين معاً، فسجّلت تحذيرَ انحرافٍ
    // لا انحرافَ وراءه — وهو تحذيرٌ يُلاحَق أياماً لأنّه يقول إنّ مساراً لا يُبلغ.
    // ========================================================================
    [Fact]
    public async Task نسختان_تطلبان_العمل_نفسه_وواحدةٌ_وحدها_تحمله()
    {
        var work = Work();
        var (firstScope, first) = await InstanceAsync();
        var (secondScope, second) = await InstanceAsync();
        await using var _ = firstScope;
        await using var __ = secondScope;

        var held = await first.TryAcquireAsync(work, TimeSpan.FromMinutes(5));
        held.Should().NotBeNull();

        var refused = await second.TryAcquireAsync(work, TimeSpan.FromMinutes(5));
        refused.Should().BeNull("العقدُ محمولٌ الآن — والرفضُ جوابٌ عاديّ لا خطأ");

        await held!.DisposeAsync();

        // وبعد الإفراج يأخذه الثاني: الإفراجُ ليس زينةً — بدونه ينتظر العاملُ التالي انتهاءَ المدّة.
        var afterRelease = await second.TryAcquireAsync(work, TimeSpan.FromMinutes(5));
        afterRelease.Should().NotBeNull();
        await afterRelease!.DisposeAsync();
    }

    // ========================================================================
    // **انتهاءُ المدّة هو آليةُ التعافي**، لا حالةُ خطأ: نسخةٌ تسقط وهي حاملةٌ للعقد لا تُفرج
    // عنه أبداً، فعقدٌ أبديّ كان سيوقف الكنسَ إلى أن يتدخّل إنسان.
    // ========================================================================
    [Fact]
    public async Task عقدٌ_انتهت_مدّته_يُؤخذ_بلا_تدخّل()
    {
        var work = Work();
        var (deadScope, dead) = await InstanceAsync();
        var (liveScope, live) = await InstanceAsync();
        await using var _ = deadScope;
        await using var __ = liveScope;

        // مدّةٌ دقيقة جداً تُحاكي نسخةً ماتت للتوّ بعد أن أخذت عقداً قصيراً.
        var held = await dead.TryAcquireAsync(work, TimeSpan.FromMilliseconds(120));
        held.Should().NotBeNull();

        (await live.TryAcquireAsync(work, TimeSpan.FromMinutes(5)))
            .Should().BeNull("ما زال ضمن مدّته");

        await Task.Delay(400);

        var recovered = await live.TryAcquireAsync(work, TimeSpan.FromMinutes(5));
        recovered.Should().NotBeNull("انتهاءُ المدّة يُحرّر العملَ بلا تدخّل");
        await recovered!.DisposeAsync();
    }

    // ========================================================================
    // التجديدُ يمدّ العقد لحامله، **ولا يمدّه لغيره**. وجوابُ `RenewAsync` هو ما يجعل العاملَ
    // يتوقّف حين يفقده — فهو `bool` لا `void` عمداً.
    // ========================================================================
    [Fact]
    public async Task التجديد_لحامله_وحده_ومَن_فقده_يعرف()
    {
        var work = Work();
        var (holderScope, holder) = await InstanceAsync();
        var (otherScope, other) = await InstanceAsync();
        await using var _ = holderScope;
        await using var __ = otherScope;

        var held = await holder.TryAcquireAsync(work, TimeSpan.FromMilliseconds(150));
        held.Should().NotBeNull();

        (await held!.RenewAsync(TimeSpan.FromMinutes(5))).Should().BeTrue("حاملُه يمدّه");
        (await other.TryAcquireAsync(work, TimeSpan.FromMinutes(5)))
            .Should().BeNull("التمديدُ وقع فعلاً، فلم ينتهِ");

        // تُحاكى الخسارة: العقدُ يُفرَج عنه ثم تأخذه النسخةُ الأخرى — ثمّ يُسأل الحاملُ الأول.
        await held.DisposeAsync();
        var stolen = await other.TryAcquireAsync(work, TimeSpan.FromMinutes(5));
        stolen.Should().NotBeNull();

        (await held.RenewAsync(TimeSpan.FromMinutes(5)))
            .Should().BeFalse("مَن لا يحمله لا يمدّه — وهذا ما يوقف العاملَ عن الكنس");

        await stolen!.DisposeAsync();
    }

    // ========================================================================
    // أعمالٌ مختلفة لا تتزاحم: كنسةٌ لا تحجب أختها. وهذا سببُ اشتقاق اسمِ العقد من اسم الكنسة
    // بدل اسمٍ واحد لها جميعاً — اسمٌ مشترك كان سيجعل مصالحةَ العدّادات تحجب مسحَ سجلّ البحث.
    // ========================================================================
    [Fact]
    public async Task عملان_مختلفان_يجريان_معاً()
    {
        var (scope, coordinator) = await InstanceAsync();
        await using var _ = scope;

        var first = await coordinator.TryAcquireAsync(Work(), TimeSpan.FromMinutes(5));
        var second = await coordinator.TryAcquireAsync(Work(), TimeSpan.FromMinutes(5));

        first.Should().NotBeNull();
        second.Should().NotBeNull();

        await first!.DisposeAsync();
        await second!.DisposeAsync();
    }

    // ========================================================================
    // **السباقُ على أوّل استعمال**: لا صفَّ للعمل بعد، وعشرُ نسخٍ تطلبه في اللحظة نفسها.
    //
    // الفهرسُ الفريد هو ما يحسمه في القاعدة، والكودُ يلتقط خرقَه ويُعيد — والنتيجةُ المطلوبة
    // **واحدٌ بالضبط**، لا «معظمُهم». وهذا هو الاختبارُ الذي لا يستطيع مُقلَّدٌ أن يجريه.
    // ========================================================================
    [Fact]
    public async Task عشرُ_نسخٍ_تتسابق_على_عملٍ_لم_يوجد_صفُّه_بعد_فيفوز_واحد()
    {
        var work = Work();
        var scopes = new List<AsyncServiceScope>();
        var locks = new List<IDistributedLock>();
        for (var i = 0; i < 10; i++)
        {
            var (scope, coordinator) = await InstanceAsync();
            scopes.Add(scope);
            locks.Add(coordinator);
        }

        try
        {
            // حاجزٌ يُلزم العشرةَ بالانطلاق معاً: بلا حاجز يبدأ الأول وينتهي قبل أن يبدأ العاشر،
            // فيمرّ الاختبارُ بلا أن يقيس تزامناً — وهو المزلق الذي تحذّر منه ADR-0049 §الالتزام
            // الرابع، وقد وقع فعلاً في هذا المستودع من قبل.
            using var barrier = new SemaphoreSlim(0, locks.Count);
            var attempts = locks.Select(async coordinator =>
            {
                await barrier.WaitAsync();
                return await coordinator.TryAcquireAsync(work, TimeSpan.FromMinutes(5));
            }).ToList();

            barrier.Release(locks.Count);
            var results = await Task.WhenAll(attempts);

            results.Count(handle => handle is not null).Should().Be(1,
                "عقدٌ واحد لعملٍ واحد — ولو فاز اثنان لجرت الكنسةُ نفسها مرّتين");

            foreach (var handle in results.Where(h => h is not null)) await handle!.DisposeAsync();

            // وصفٌّ واحد في القاعدة رغم عشر محاولات إنشاء: الفهرسُ الفريد أدّى دوره.
            await using var verify = _factory.Services.CreateAsyncScope();
            var db = verify.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.DistributedLeases.CountAsync(l => l.Name == work)).Should().Be(1);
        }
        finally
        {
            foreach (var scope in scopes) await scope.DisposeAsync();
        }
    }
}
