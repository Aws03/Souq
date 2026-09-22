using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Tenancy;
using Souq.Domain.Platform;
using Souq.IntegrationTests.Infrastructure;

namespace Souq.IntegrationTests;

// ============================================================================
// إشاراتُ إبطال الذاكرات بين النسخ، على SQL Server حقيقيّ (C4، [ADR-0057](0057)).
//
// **وما يُقاس هنا هو الجزءُ الذي لا يراه أيُّ اختبارِ وحدة**: أنّ نسختين تتشاركان عدّاداً في
// القاعدة فعلاً، وأنّ زيادتَه ذرّيّة. أمّا تطبيقُ الإشارة على الذاكرة المحلّية فمنطقٌ في
// `CacheSignalWatcher` — وهو الجزءُ السهل؛ الصعبُ هو أن تصل الزيادةُ أصلاً.
//
// **ولماذا لا يُقاس المراقبُ نفسه بانتظارِ خمس ثوانٍ؟** لأنّ اختباراً ينام حتى تمرّ دورةٌ خلفية
// يقيس المؤقّتَ لا السلوك، ويتذبذب على مضيفٍ مشغول. فما يُقاس هنا هو العقدُ الذي يقوم عليه
// المراقب: الزيادةُ تُنشَر، وتُقرأ، ولا تضيع تحت التزامن.
// ============================================================================
[Collection(IntegrationCollection.Name)]
public class CacheSignalTests
{
    private readonly SouqApiFactory _factory;

    public CacheSignalTests(SouqApiFactory factory) => _factory = factory;

    private async Task<long> VersionOfAsync(string signal)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var signals = scope.ServiceProvider.GetRequiredService<ICacheSignals>();
        var all = await signals.ReadAllAsync();
        return all.TryGetValue(signal, out var version) ? version : 0;
    }

    private async Task BumpAsync(string signal)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ICacheSignals>().BumpAsync(signal);
    }

    [Fact]
    public async Task الإشارة_تُنشَر_وتُقرأ_من_نطاقٍ_آخر()
    {
        // نطاقان مختلفان يُحاكيان نسختين: ما تكتبه إحداهما تقرؤه الأخرى من القاعدة لا من ذاكرتها.
        var before = await VersionOfAsync(CacheSignal.TenantDirectory);

        await BumpAsync(CacheSignal.TenantDirectory);

        (await VersionOfAsync(CacheSignal.TenantDirectory)).Should().BeGreaterThan(before);
    }

    [Fact]
    public async Task الإشارتان_مستقلّتان()
    {
        // إبطالُ دليل المتاجر لا يُسقط أختامَ الجلسات: ذاكرتان مختلفتان، وخلطُهما كان سيجعل كلَّ
        // تغييرِ إعدادٍ في متجرٍ يُجبر كلَّ نسخةٍ على إعادة قراءة ختمِ كلِّ حسابٍ نشط.
        var sessionsBefore = await VersionOfAsync(CacheSignal.SessionStamps);

        await BumpAsync(CacheSignal.TenantDirectory);

        (await VersionOfAsync(CacheSignal.SessionStamps)).Should().Be(sessionsBefore);
    }

    // ========================================================================
    // **القفزاتُ لا تضيع تحت التزامن** — وهذا هو سببُ كون الزيادة جملةً ذرّية واحدة.
    //
    // قراءةٌ ثمّ كتابة كانت ستجعل نسختين تُبطلان معاً تكتبان القيمةَ نفسها، فتضيع إحدى القفزتين؛
    // ونسخةٌ ثالثة رأت القيمةَ الوسطى لا تعرف أنّ شيئاً آخر تغيّر — وهو بالضبط التقادمُ الذي
    // وُجدت الإشارةُ لتُنهيه.
    // ========================================================================
    [Fact]
    public async Task عشرُ_إبطالاتٍ_متزامنة_تُنتج_عشرَ_قفزات_بلا_ضياع()
    {
        var before = await VersionOfAsync(CacheSignal.TenantDirectory);

        // حاجزٌ يُلزم العشرةَ بالانطلاق معاً: بلا حاجزٍ ينتهي الأوّل قبل أن يبدأ العاشر، فيمرّ
        // الاختبارُ بلا أن يقيس تزامناً — وهو المزلق الذي تحذّر منه ADR-0049 §الالتزام الرابع.
        using var barrier = new SemaphoreSlim(0, 10);
        var bumps = Enumerable.Range(0, 10).Select(async _ =>
        {
            await barrier.WaitAsync();
            await BumpAsync(CacheSignal.TenantDirectory);
        }).ToList();

        barrier.Release(10);
        await Task.WhenAll(bumps);

        (await VersionOfAsync(CacheSignal.TenantDirectory)).Should().Be(before + 10);
    }

    // ========================================================================
    // إبطالُ الدليل من حالةِ استخدامٍ حقيقية يُنشر: ليس نداءً في اختبار، بل ما يفعله تغييرُ
    // إعدادِ متجرٍ أو تعليقُه فعلاً — وهو الطريق الذي يجعل تعليق متجرٍ يصل إلى نسخةٍ أخرى في
    // ثوانٍ بدل دقيقة.
    // ========================================================================
    [Fact]
    public async Task إبطالُ_دليل_المتاجر_يرفع_إشارتَه()
    {
        var before = await VersionOfAsync(CacheSignal.TenantDirectory);

        await using (var scope = _factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<ITenantDirectory>().InvalidateAsync();

        (await VersionOfAsync(CacheSignal.TenantDirectory)).Should().BeGreaterThan(before);
    }

    [Fact]
    public async Task إشارةٌ_غير_معروفة_تُرفض_ولا_تُنشَر()
    {
        // الأسماءُ ثوابتُ في `CacheSignal`، فنصٌّ يُمرَّر بيدٍ خطأٌ برمجيّ يُرفض عند حدّه لا
        // يُكتب صفّاً لا يقرؤه أحد.
        var bump = async () => await BumpAsync("not-a-real-cache");
        await bump.Should().ThrowAsync<Souq.Domain.Exceptions.InvalidLeaseException>();
    }
}
