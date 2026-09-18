using System.Net;
using System.Net.Http.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AwesomeAssertions;
using Souq.Application.Features.Products.Commands;
using Souq.Domain.Entities;
using Souq.IntegrationTests.Infrastructure;

namespace Souq.IntegrationTests;

// ============================================================================
// سجلّ البحث من طرفه إلى طرفه (M13) — وهو الجزء الذي لا تُثبته وحدةٌ واحدة، لأنّ سبب وجوده كلّه في الحدود:
//
//   1) **الكتابة غير متزامنة**: الطلب يعود قبل أن يُحفظ السطر. اختبارُ وحدةٍ يتحقّق أنّ `Record` نُوديت،
//      ولا يُثبت أنّ صفّاً وصل القاعدة — ولا أنّه وصل **بالمتجر الصحيح**، وهو أخطر ما في المرحلة: الكاتب
//      خلفيّ بلا طلبٍ ولا سياق، وختمُ `TenantId` يعتمد على نطاقٍ يُنشئه هو بنفسه.
//   2) **التجميع في SQL**: `GROUP BY`/`HAVING` وترتيبٌ وترقيمٌ — سلوك SQL Server لا سلوك LINQ في الذاكرة.
//   3) **سياسة الحفظ**: حذفٌ مجمَّع يتجاوز حارس الكتابة، فالمرشّح وحده يحصره في متجره.
//
// وانتظارُ الكتابة هنا استقصاءٌ بمهلة (`WaitForAsync`) لا نوم: النافذة عشرون مللي ثانية في مضيف الاختبار،
// والاستقصاء يجعل الاختبار سريعاً حين يعمل الكاتب، ويفشل بوضوح حين لا يعمل.
// ============================================================================
[Collection(IntegrationCollection.Name)]
public class SearchAnalyticsTests
{
    private readonly SouqApiFactory _factory;
    private readonly TestApi _api;

    public SearchAnalyticsTests(SouqApiFactory factory)
    {
        _factory = factory;
        _api = new TestApi(factory);
    }

    private sealed record InsightRow(
        string Culture, string Term, string TermNormalized,
        int Searches, int ZeroResultSearches, DateTime LastSearchedAt, bool NeverFoundAnything);

    private sealed record Summary(int TotalSearches, int DistinctTerms, int ZeroResultSearches);

    private sealed record InsightsPage(List<InsightRow> Items, int TotalCount, int PageNumber, Summary Summary);

    // ── أدوات ──────────────────────────────────────────────────────────────────

    // كلمةٌ فريدة لكل اختبار: المتجر الافتراضي مشترك بين كل اختبارات المجموعة، فسجلّه يتراكم. وبلا تفريدٍ
    // يرى اختبارٌ أسطر اختبارٍ آخر ويصير أخضر أو أحمر بترتيب التشغيل لا بصحّته.
    private static string UniqueTerm(string label) => $"زقم{label}{Guid.NewGuid().ToString("N")[..6]}";

    private static async Task SearchAsync(HttpClient client, string term, int times = 1)
    {
        for (var i = 0; i < times; i++)
            (await client.GetAsync($"api/products?keyword={Uri.EscapeDataString(term)}"))
                .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // استقصاءٌ بمهلة على الكاتب الخلفي. المهلة سخيّة والدورة قصيرة: النجاح فوريّ عملياً، والفشل صريح.
    private static async Task<T> WaitForAsync<T>(Func<Task<T>> read, Func<T, bool> until, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        T last;
        do
        {
            last = await read();
            if (until(last)) return last;
            await Task.Delay(25);
        } while (DateTime.UtcNow < deadline);

        throw new TimeoutException($"لم يكتب الكاتب الخلفي {what} خلال المهلة. آخر ما قُرئ: {last}");
    }

    private Task<int> CountAsync(TestApi api, string normalized) =>
        api.WithDbAsync(db => db.SearchQueryLogs.CountAsync(l => l.TermNormalized == normalized));

    // ── مسار الكتابة ───────────────────────────────────────────────────────────

    // ============================================================================
    // الاختبار الأساسي للمرحلة: بحثٌ من متجرٍ يُنتج صفّاً **في ذلك المتجر**، بعدد نتائجه وصورته المطبَّعة.
    //
    // و`TenantId` هو ما يُفحص أوّلاً: الكاتب يعمل خارج أي طلب، وصحّة الصفّ كلّها في أنّه أنشأ نطاق المتجر
    // الصحيح قبل الحفظ. لو حُفظ خارج نطاق لرُفض (الحارس)، ولو حُفظ في نطاق الخطأ لظهر سجلّ متجرٍ في آخر.
    // ============================================================================
    [Fact]
    public async Task بحثُ_المتسوّق_يُكتب_صفّاً_في_متجره_بعدد_نتائجه()
    {
        var store = await _factory.CreateStoreAsync();
        var api = _api.ForStore(store);
        var term = UniqueTerm("كتب");

        await SearchAsync(api.Anonymous(), term);

        var rows = await WaitForAsync(
            () => api.WithDbAsync(db => db.SearchQueryLogs
                .Where(l => l.Term == term).AsNoTracking().ToListAsync()),
            r => r.Count > 0, $"سطر البحث عن \"{term}\"");

        rows.Should().HaveCount(1);
        var row = rows[0];
        row.TenantId.Should().Be(store.Tenant.Id, "الكاتب الخلفي يحفظ داخل نطاق متجر البحث لا خارجه");
        row.Term.Should().Be(term);
        row.TermNormalized.Should().Be(Souq.Domain.ValueObjects.SearchText.Normalize(term));
        row.ResultCount.Should().Be(0, "الكلمة مُفرَّدة فلا منتج يطابقها");
        row.FoundNothing.Should().BeTrue();
        row.SearchedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    // بحثٌ وجد نتائج يُسجَّل بعددها لا بصفر — وإلا صارت كل الكلمات تستحقّ تصرّفاً ولم يصلح السجلّ لشيء.
    [Fact]
    public async Task بحثٌ_وجد_نتائج_يُسجَّل_بعددها()
    {
        var store = await _factory.CreateStoreAsync();
        var api = _api.ForStore(store);
        var admin = await api.AdminAsync();
        var term = UniqueTerm("وجد");
        var categoryId = await api.CreateCategoryAsync(admin);
        await api.CreateProductAsync(admin, categoryId, name: $"منتج {term}");

        await SearchAsync(api.Anonymous(), term);

        var row = await WaitForAsync(
            () => api.WithDbAsync(db => db.SearchQueryLogs
                .Where(l => l.Term == term).AsNoTracking().FirstOrDefaultAsync()),
            r => r is not null, $"سطر البحث عن \"{term}\"");

        row!.ResultCount.Should().Be(1);
        row.FoundNothing.Should().BeFalse();
    }

    // التصفّح ليس بحثاً: قائمةٌ بلا كلمة لا تُنتج سطراً، وإلا امتلأ الجدول بما لا يُقرأ.
    [Fact]
    public async Task التصفّح_بلا_كلمة_لا_يُنتج_سطراً()
    {
        var store = await _factory.CreateStoreAsync();
        var api = _api.ForStore(store);

        (await api.Anonymous().GetAsync("api/products")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await api.Anonymous().GetAsync("api/products?keyword=%20%20")).StatusCode.Should().Be(HttpStatusCode.OK);

        // مهلةٌ قصيرة تكفي نافذةَ عشرين مللي ثانية عدّة مرّات: لو كان سيُكتب شيء لكُتب.
        await Task.Delay(300);
        (await api.WithDbAsync(db => db.SearchQueryLogs.CountAsync())).Should().Be(0);
    }

    // ============================================================================
    // العزل بين المتاجر — وهو ما يجعل السجلّ صالحاً للعرض أصلاً: تاجرٌ يرى كلمات زبائن تاجرٍ آخر تسريبٌ
    // تجاري، وليس خطأ عرض. يُفحص من الطرفين: الصفّ في القاعدة، والشاشة عبر HTTP.
    // ============================================================================
    [Fact]
    public async Task سجلّ_متجرٍ_لا_يظهر_في_متجرٍ_آخر()
    {
        var first = await _factory.CreateStoreAsync();
        var second = await _factory.CreateStoreAsync();
        var firstApi = _api.ForStore(first);
        var secondApi = _api.ForStore(second);
        var term = UniqueTerm("عزل");
        var normalized = Souq.Domain.ValueObjects.SearchText.Normalize(term);

        await SearchAsync(firstApi.Anonymous(), term, times: 2);
        await WaitForAsync(() => CountAsync(firstApi, normalized), c => c == 2, $"سطري \"{term}\"");

        (await CountAsync(secondApi, normalized)).Should().Be(0, "مرشّح المستأجر يحصر السجلّ في متجره");

        var insights = await (await secondApi.AdminAsync())
            .GetFromJsonAsync<InsightsPage>("api/admin/search-synonyms/insights", TestApi.Json);
        insights!.Items.Should().NotContain(i => i.TermNormalized == normalized);
    }

    // ── شاشة التاجر ────────────────────────────────────────────────────────────

    // ============================================================================
    // التجميع: أسطرٌ كثيرة لكلمةٍ واحدة تصير صفّاً واحداً بعددها — والصور المختلفة للكلمة نفسها تُجمَع معاً
    // (التجميع على الصورة المطبَّعة)، وهو الفرق بين شاشةٍ تُفيد وقائمةٍ من أسطر خامّة.
    // ============================================================================
    [Fact]
    public async Task الكلمة_تُجمَع_بصورتها_المطبَّعة_ويُعرض_أحدث_ما_كُتب()
    {
        var store = await _factory.CreateStoreAsync();
        var api = _api.ForStore(store);
        var stem = UniqueTerm("جمع");
        var normalized = Souq.Domain.ValueObjects.SearchText.Normalize(stem);

        // نفس الكلمة بصورتين تُطبَّعان إلى واحدة (`SearchText.Normalize` يُنزل الحالة) — والصورة الثانية
        // هي **الأخيرة** زمناً، فهي التي يجب أن تُعرض.
        var lastTyped = stem.ToUpperInvariant();
        lastTyped.Should().NotBe(stem, "الاختبار يقوم على اختلاف الصورتين المكتوبتين");
        await SearchAsync(api.Anonymous(), stem, times: 2);
        await SearchAsync(api.Anonymous(), lastTyped);
        await WaitForAsync(() => CountAsync(api, normalized), c => c == 3, $"ثلاثة أسطر لـ \"{stem}\"");

        var page = await (await api.AdminAsync())
            .GetFromJsonAsync<InsightsPage>("api/admin/search-synonyms/insights", TestApi.Json);

        var row = page!.Items.Should().ContainSingle(i => i.TermNormalized == normalized).Subject;
        row.Searches.Should().Be(3, "ثلاثة بحوث لكلمةٍ واحدة صفٌّ واحد بعدد ثلاثة");
        row.Term.Should().Be(lastTyped, "المعروض أحدث صورةٍ كتبها زبون، لا أوّلها ولا أبجديّها");
        row.ZeroResultSearches.Should().Be(3);
        row.NeverFoundAnything.Should().BeTrue();
        page.Summary.TotalSearches.Should().BeGreaterThanOrEqualTo(3);
    }

    // ============================================================================
    // مرشّح "لم تجد شيئاً": الكلمة التي تفشل **دائماً** تستحقّ تصرّفاً، والتي تفشل أحياناً لا تستحقّه — فرقٌ
    // يُقارن مُجمَّعين (HAVING) لا صفوفاً، وهو أرجح مواضع الخطأ في هذا الاستعلام.
    // ============================================================================
    [Fact]
    public async Task مرشّح_بلا_نتيجة_يستثني_الكلمة_التي_تنجح_أحياناً()
    {
        var store = await _factory.CreateStoreAsync();
        var api = _api.ForStore(store);
        var admin = await api.AdminAsync();
        var sometimes = UniqueTerm("أحيانا");
        var never = UniqueTerm("أبدا");
        var categoryId = await api.CreateCategoryAsync(admin);

        // بحثٌ أول قبل وجود المنتج: بلا نتيجة.
        await SearchAsync(api.Anonymous(), sometimes);
        await SearchAsync(api.Anonymous(), never, times: 2);
        await WaitForAsync(
            () => CountAsync(api, Souq.Domain.ValueObjects.SearchText.Normalize(never)), c => c == 2, "سطري الكلمة الفاشلة");

        // ثم يُضاف المنتج ويُبحث ثانيةً: نفس الكلمة نجحت هذه المرّة.
        await api.CreateProductAsync(admin, categoryId, name: $"منتج {sometimes}");
        await SearchAsync(api.Anonymous(), sometimes);
        await WaitForAsync(
            () => CountAsync(api, Souq.Domain.ValueObjects.SearchText.Normalize(sometimes)), c => c == 2, "سطري الكلمة المتقلّبة");

        var all = await admin.GetFromJsonAsync<InsightsPage>(
            "api/admin/search-synonyms/insights?pageSize=100", TestApi.Json);
        var failing = await admin.GetFromJsonAsync<InsightsPage>(
            "api/admin/search-synonyms/insights?onlyZeroResults=true&pageSize=100", TestApi.Json);

        var sometimesNormalized = Souq.Domain.ValueObjects.SearchText.Normalize(sometimes);
        var neverNormalized = Souq.Domain.ValueObjects.SearchText.Normalize(never);

        all!.Items.Should().Contain(i => i.TermNormalized == sometimesNormalized)
            .And.Contain(i => i.TermNormalized == neverNormalized);

        var sometimesRow = all.Items.Single(i => i.TermNormalized == sometimesNormalized);
        sometimesRow.Searches.Should().Be(2);
        sometimesRow.ZeroResultSearches.Should().Be(1, "فشلت مرّةً ونجحت مرّة");
        sometimesRow.NeverFoundAnything.Should().BeFalse();

        failing!.Items.Should().Contain(i => i.TermNormalized == neverNormalized)
            .And.NotContain(i => i.TermNormalized == sometimesNormalized,
                "كلمةٌ تجد نتائج أحياناً ليست عملاً على التاجر");

        // والملخّص يعدّ النافذة كلّها لا الصفحة، ولا يتأثّر بمرشّح الصفحة.
        failing.Summary.TotalSearches.Should().Be(all.Summary.TotalSearches);
        failing.Summary.ZeroResultSearches.Should().Be(3);
    }

    // الترتيب: الأكثر بحثاً أولاً. بلا ذلك تصير الشاشة قائمةً عشوائية لا تُقرأ.
    [Fact]
    public async Task الأكثر_بحثاً_أولاً()
    {
        var store = await _factory.CreateStoreAsync();
        var api = _api.ForStore(store);
        var many = UniqueTerm("كثير");
        var few = UniqueTerm("قليل");

        await SearchAsync(api.Anonymous(), few);
        await SearchAsync(api.Anonymous(), many, times: 3);
        await WaitForAsync(
            () => CountAsync(api, Souq.Domain.ValueObjects.SearchText.Normalize(many)), c => c == 3, "أسطر الكلمة الأكثر");

        var page = await (await api.AdminAsync())
            .GetFromJsonAsync<InsightsPage>("api/admin/search-synonyms/insights?pageSize=100", TestApi.Json);

        var terms = page!.Items.Select(i => i.TermNormalized).ToList();
        terms.IndexOf(Souq.Domain.ValueObjects.SearchText.Normalize(many))
            .Should().BeLessThan(terms.IndexOf(Souq.Domain.ValueObjects.SearchText.Normalize(few)));
    }

    // النافذة الزمنية تعمل: سطرٌ أقدم من المطلوب لا يُحتسب.
    [Fact]
    public async Task النافذة_الزمنية_تستثني_ما_هو_أقدم_منها()
    {
        var store = await _factory.CreateStoreAsync();
        var api = _api.ForStore(store);
        var term = UniqueTerm("قديم");
        var normalized = Souq.Domain.ValueObjects.SearchText.Normalize(term);

        await SearchAsync(api.Anonymous(), term);
        await WaitForAsync(() => CountAsync(api, normalized), c => c == 1, "سطر الكلمة");

        // يُقدَّم السطر عشرة أيام إلى الماضي.
        await api.WithDbAsync(db => db.SearchQueryLogs
            .Where(l => l.TermNormalized == normalized)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.SearchedAt, l => l.SearchedAt.AddDays(-10))));

        var admin = await api.AdminAsync();
        var lastWeek = await admin.GetFromJsonAsync<InsightsPage>(
            "api/admin/search-synonyms/insights?days=7&pageSize=100", TestApi.Json);
        var lastMonth = await admin.GetFromJsonAsync<InsightsPage>(
            "api/admin/search-synonyms/insights?days=30&pageSize=100", TestApi.Json);

        lastWeek!.Items.Should().NotContain(i => i.TermNormalized == normalized);
        lastMonth!.Items.Should().Contain(i => i.TermNormalized == normalized);
    }

    // الشاشة للتاجر وحده: نفس صلاحية الكتالوج، والعميل والزائر خارجها.
    [Fact]
    public async Task شاشة_أثر_البحث_للتاجر_وحده()
    {
        var store = await _factory.CreateStoreAsync();
        var api = _api.ForStore(store);
        const string path = "api/admin/search-synonyms/insights";

        (await api.Anonymous().GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var (customer, _) = await api.NewCustomerAsync();
        (await customer.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── سياسة الحفظ ────────────────────────────────────────────────────────────

    // ============================================================================
    // المسح يحذف ما تجاوز المدّة **ويترك ما دونها** — والنصف الثاني هو الذي يُختبر هنا فعلاً: مسحٌ يحذف
    // القديم صحيحٌ وقد يكون كارثةً لو حذف الجديد معه، وجملةُ حذفٍ مجمَّعة بشرطٍ خاطئ تفعل ذلك بلا صوت.
    // ============================================================================
    [Fact]
    public async Task المسح_يحذف_ما_تجاوز_مدّة_الحفظ_ويترك_ما_دونها()
    {
        var store = await _factory.CreateStoreAsync();
        var api = _api.ForStore(store);
        var old = UniqueTerm("مسح");
        var fresh = UniqueTerm("حديث");
        var oldNormalized = Souq.Domain.ValueObjects.SearchText.Normalize(old);
        var freshNormalized = Souq.Domain.ValueObjects.SearchText.Normalize(fresh);

        await SearchAsync(api.Anonymous(), old);
        await SearchAsync(api.Anonymous(), fresh);
        await WaitForAsync(() => CountAsync(api, freshNormalized), c => c == 1, "السطر الحديث");
        await WaitForAsync(() => CountAsync(api, oldNormalized), c => c == 1, "السطر القديم");

        // سنةٌ إلى الماضي: أبعد من الحفظ الافتراضي (تسعون يوماً) بكثير.
        await api.WithDbAsync(db => db.SearchQueryLogs
            .Where(l => l.TermNormalized == oldNormalized)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.SearchedAt, l => l.SearchedAt.AddDays(-365))));

        var deleted = await PurgeAsync(store);

        deleted.Should().BeGreaterThanOrEqualTo(1);
        (await CountAsync(api, oldNormalized)).Should().Be(0, "ما تجاوز المدّة يُحذف");
        (await CountAsync(api, freshNormalized)).Should().Be(1, "وما دونها يبقى");
    }

    // ============================================================================
    // والمسح لا يتجاوز متجره: الحذف المجمَّع يتخطّى حارس الكتابة، فمرشّح المستأجر دفاعه الوحيد. لو نُفِّذ
    // خارج نطاق متجرٍ لأفرغ الجدول للجميع — وهذا الاختبار هو ما يمنع ذلك من المرور صامتاً.
    // ============================================================================
    [Fact]
    public async Task مسح_متجرٍ_لا_يلمس_سجلّ_متجرٍ_آخر()
    {
        var mine = await _factory.CreateStoreAsync();
        var other = await _factory.CreateStoreAsync();
        var myApi = _api.ForStore(mine);
        var otherApi = _api.ForStore(other);
        var term = UniqueTerm("جار");
        var normalized = Souq.Domain.ValueObjects.SearchText.Normalize(term);

        await SearchAsync(myApi.Anonymous(), term);
        await SearchAsync(otherApi.Anonymous(), term);
        await WaitForAsync(() => CountAsync(myApi, normalized), c => c == 1, "سطري");
        await WaitForAsync(() => CountAsync(otherApi, normalized), c => c == 1, "سطر الجار");

        // كلا السطرين يصيران قديمين، ثم يُمسح متجري وحده.
        foreach (var api in new[] { myApi, otherApi })
            await api.WithDbAsync(db => db.SearchQueryLogs
                .Where(l => l.TermNormalized == normalized)
                .ExecuteUpdateAsync(s => s.SetProperty(l => l.SearchedAt, l => l.SearchedAt.AddDays(-365))));

        await PurgeAsync(mine);

        (await CountAsync(myApi, normalized)).Should().Be(0);
        (await CountAsync(otherApi, normalized)).Should().Be(1, "سجلّ الجار القديم ليس عمل مسحي");
    }

    private async Task<int> PurgeAsync(TestStore store)
    {
        await using var scope = await _factory.TenantScopeAsync(store.Tenant);
        return await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new PurgeSearchLogCommand());
    }

    // ============================================================================
    // ولا بيانات شخصية في الجدول — يُفحص على **المخطَّط المُهاجَر** لا على الصنف: المرحلة بنت سياسة حفظها
    // على أنّ لا شخصيّ فيه (وهو ما أعفاها من انتظار جواب TD-16 القانوني). عمودٌ يُضاف يوماً بمعرّف زائر أو
    // عنوانٍ يُبطل ذلك الأساس بلا أن ينتبه أحد، فهذا الاختبار هو الذي ينتبه.
    // ============================================================================
    [Fact]
    public async Task جدول_السجلّ_لا_يحمل_عموداً_شخصياً()
    {
        var expected = new[]
        {
            "Id", "TenantId", "Culture", "Term", "TermNormalized", "ResultCount", "SearchedAt",
            "CreatedAt", "UpdatedAt",
        };

        var columns = await _api.WithDbAsync(db => Task.FromResult(
            db.Model.FindEntityType(typeof(SearchQueryLog))!
                .GetProperties().Select(p => p.Name).OrderBy(n => n).ToList()));

        columns.Should().BeEquivalentTo(expected,
            "عمودٌ جديد في هذا الجدول يحتاج مراجعةً صريحة: أيّ بيانٍ شخصي فيه يُبطل أساس سياسة حفظه");
    }
}
