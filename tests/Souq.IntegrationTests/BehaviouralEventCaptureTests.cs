using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AwesomeAssertions;
using Souq.Application.Features.Analytics;
using Souq.Domain.Entities;
using Souq.IntegrationTests.Infrastructure;

namespace Souq.IntegrationTests;

// ============================================================================
// الالتقاط السلوكي عبر الخادم الحقيقي (C9، ADR-0050).
//
// **أهمّ ما يُثبَت هنا هو ألّا يحدث شيء.** قرار المالك C-08 = A أذن بمعرّف زائر مُعتِم وأبقى ثلاثة
// أجوبة للمالك «قبل أن يُكتب أوّل صفّ»: الأساس القانوني، ومدّة الحفظ، والإقامة. فالبناء كلّه
// مُشحونٌ معطّلاً، ومعطّلاً يعني: **لا صفّ في القاعدة، ولا ملفّ تعريف ارتباط على متصفّح أحد** —
// وهذا ما يُقاس هنا على نشرٍ حقيقيّ لا بقراءة الكود.
//
// والثاني: الجدول **لا يحمل عموداً لمعرّف عميل**. نظيرُ هذا الاختبار على `SearchQueryLogs` هو ما
// حرس الأساسَ القانوني لمدّة حفظه، وهذا الجدول أخطر: فيه معرّف زائر. فصلُ الهويّة هو ما يجعل طلبَ
// المحو حذفاً من جدول واحد، وعمودٌ واحد يُضاف هنا يُنهي ذلك بلا أن ينبّه أحداً.
// ============================================================================
[Collection(IntegrationCollection.Name)]
public class BehaviouralEventCaptureTests
{
    private readonly SouqApiFactory _factory;
    private readonly TestApi _api;

    public BehaviouralEventCaptureTests(SouqApiFactory factory)
    {
        _factory = factory;
        _api = new TestApi(factory);
    }

    // ============================================================================
    // الالتقاط معطّل (وهو حال المجموعة كلّها، وحالُ كل نشرٍ حتى يُجيب المالك): بحثُ متسوّقٍ
    // يعمل كاملاً، ولا يُكتب حدثٌ واحد، ولا يُصكّ معرّف تنفيذ بحث، ولا يُوضع ملفّ على متصفّحه.
    // ============================================================================
    [Fact]
    public async Task الالتقاط_معطّلاً_لا_يُكتب_حدث_ولا_يُوضع_ملفّ_ولا_يُصكّ_معرّف_بحث()
    {
        var store = await _factory.CreateStoreAsync();
        var api = _api.ForStore(store);
        var admin = await api.AdminAsync();
        await api.CreateProductAsync(admin, price: 15m, stock: 4);

        var response = await api.Anonymous().GetAsync("/api/products?keyword=قميص");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // لا معرّف تنفيذ بحث في الردّ: لا معرّف بلا حدثٍ يحمله.
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        (body.RootElement.TryGetProperty("searchExecutionId", out var id) && id.ValueKind != JsonValueKind.Null)
            .Should().BeFalse("الالتقاط معطّل، فلا معرّف يُصكّ");

        // ولا ملفّ تعريف ارتباط للزائر ولا للجلسة — لا شيء يُوضع على متصفّح زائرٍ لم يُقاس.
        var cookies = response.Headers.TryGetValues("Set-Cookie", out var values) ? values.ToList() : [];
        cookies.Should().NotContain(c => c.Contains(VisitorCookieNames.Visitor, StringComparison.Ordinal));
        cookies.Should().NotContain(c => c.Contains(VisitorCookieNames.Session, StringComparison.Ordinal));

        // ولا صفّ في الجدول، ولو بعد انتظارٍ يكفي لدورة كتابة.
        await Task.Delay(300);
        var rows = await _api.WithDbAsync(db => db.BehaviouralEvents.IgnoreQueryFilters()
            .CountAsync(e => e.TenantId == store.Tenant.Id));
        rows.Should().Be(0, "الالتقاط معطّل حتى يُجيب المالك على أسئلة C-08 الثلاثة");
    }

    // ============================================================================
    // أعمدةُ الجدول مثبَّتةٌ بقيمتها، كما هي أعمدةُ `SearchQueryLogs` — **ولا عمودَ لمعرّف عميل
    // بينها**.
    //
    // ولأنّ هذا الجدول يحمل معرّف زائر، فهو أخطر من ذاك: الفصلُ بين الحدث والهويّة هو ما يجعل
    // المحوَ حذفاً من `VisitorIdentityLinks` وحده. وعمودٌ واحد يُضاف هنا — بحسن نيّة، تسهيلاً
    // لوصلٍ في تقرير — يُنهي ذلك ويجعل المحو إعادةَ كتابة مخزنٍ لا يُكتب إلا إضافةً.
    // ============================================================================
    [Fact]
    public async Task جدول_الأحداث_لا_يحمل_عمود_عميل()
    {
        var expected = new[]
        {
            "Id", "TenantId", "EventId", "Name", "SchemaVersion", "OccurredAt", "ReceivedAt",
            "VisitorId", "SessionId", "SearchExecutionId", "CorrelationId", "Surface", "Culture", "Payload",
            "CreatedAt", "UpdatedAt",
        };

        var columns = await _api.WithDbAsync(db => Task.FromResult(
            db.Model.FindEntityType(typeof(BehaviouralEvent))!
                .GetProperties().Select(p => p.Name).OrderBy(n => n).ToList()));

        columns.Should().BeEquivalentTo(expected,
            "عمودٌ يُضاف أو يُحذف هنا قرارٌ يخصّ بياناتٍ شخصية — ولا عمودَ لمعرّف عميل أبداً (ADR-0050 §5)");
    }

    // الربطُ في جدولٍ منفصل بمفتاحٍ للعميل: هو الموضع **الوحيد** الذي يجتمع فيه الزائر والعميل،
    // فطلبُ المحو حذفٌ من هنا لا إعادةُ كتابةٍ هناك.
    [Fact]
    public async Task ربط_الهويّة_جدول_منفصل_وله_مسار_محو_بمفتاح()
    {
        var link = await _api.WithDbAsync(db => Task.FromResult(
            db.Model.FindEntityType(typeof(VisitorIdentityLink))!));

        link.GetProperties().Select(p => p.Name).Should()
            .Contain(["VisitorId", "CustomerId", "LinkedAt"]);

        // فهرسٌ على (متجر، عميل): محوُ عميلٍ حذفٌ بمفتاح لا مسحُ جدول.
        link.GetIndexes().Should().Contain(i =>
            i.Properties.Select(p => p.Name).SequenceEqual(new[] { "TenantId", "CustomerId" }));
    }

    // ============================================================================
    // **والمسار يعمل حين يُضبَط** — على نشرٍ مُفعَّلٍ ببناءٍ مشتقّ، لا بقراءة الكود.
    //
    // وهذا هو الاختبار الذي يُثبت أنّ ما بُني ليس هيكلاً معطّلاً: بحثُ متسوّقٍ يصكّ معرّف تنفيذه
    // ويُعيده في الردّ، ويُكتب صفٌّ بغلافه كاملاً، ويُوضع ملفّا الزائر والجلسة **غيرَ ضروريَّين**
    // (`IsEssential = false`)، ثم يُجمَّع اليوم ويتقدّم المسح إلى حدّه.
    //
    // ويُشغَّل ببناءٍ مشتقّ كي تبقى بقيّةُ المجموعة على الحال الافتراضي: معطّلاً.
    // ============================================================================
    [Fact]
    public async Task الالتقاط_مضبوطاً_يكتب_الحدث_ويُعيد_معرّف_البحث_ويضع_ملفَّين_غير_ضروريَّين()
    {
        await using var enabled = _factory.WithWebHostBuilder(b =>
        {
            b.UseSetting($"{EventCaptureSettings.SectionName}:Enabled", "true");
            b.UseSetting($"{EventCaptureSettings.SectionName}:VisitorIdentifierEnabled", "true");
            b.UseSetting($"{EventCaptureSettings.SectionName}:RetentionDays", "30");
            b.UseSetting($"{EventCaptureSettings.SectionName}:LawfulBasis", "test-basis");
            // نافذةُ كتابةٍ قصيرة: الاختبار ينتظر صفّاً، لا ثانيتين.
            b.UseSetting($"{EventCaptureSettings.SectionName}:WriteBatchMilliseconds", "50");
            // المَسْحان معطّلان: هذا الاختبار يستدعيهما بنفسه، فمَسْحٌ يعمل في الخلفية يسابقه.
            b.UseSetting($"{EventCaptureSettings.SectionName}:RollupIntervalMinutes", "0");
            b.UseSetting($"{EventCaptureSettings.SectionName}:PurgeIntervalMinutes", "0");
        });

        var store = await _factory.CreateStoreAsync();
        var client = enabled.CreateClient();
        client.BaseAddress = new Uri($"http://{store.Host}");

        var response = await client.GetAsync("/api/products?keyword=قميص");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var echoed = body.RootElement.GetProperty("searchExecutionId").GetGuid();
        echoed.Should().NotBe(Guid.Empty, "المعرّف يُصكّ عند الاستعلام ويُعاد كي يردّه العميل");

        // الملفّان موجودان، وكلاهما HttpOnly وغيرُ ضروريّ — والثاني هو الفرق عن ملفّ السلة.
        var cookies = response.Headers.GetValues("Set-Cookie").ToList();
        cookies.Should().Contain(c => c.Contains(VisitorCookieNames.Visitor, StringComparison.Ordinal) && c.Contains("httponly", StringComparison.OrdinalIgnoreCase));
        cookies.Should().Contain(c => c.Contains(VisitorCookieNames.Session, StringComparison.Ordinal));

        // والصفّ يُكتب بغلافه: الاسم، والمعرّف المصكوك نفسه، والسطح، وبلا معرّف عميل.
        var written = await WaitForEventAsync(store.Tenant.Id);
        written.Name.Should().Be(BehaviouralEventNames.SearchExecuted);
        written.SearchExecutionId.Should().Be(echoed, "الحدث والردّ يحملان المعرّف نفسه، وهو ما ينسب المبيعة لاحقاً");
        written.Surface.Should().Be(BehaviouralSurfaces.Storefront);
        written.VisitorId.Should().NotBeNullOrEmpty();
        written.Payload.Should().Contain("قميص");

        // ثم التجميع والمسح: اليومُ الجاري لا يُجمَّع (أحداثه لم تنتهِ)، فلا علامة ولا مسح.
        await using var scope = await _factory.TenantScopeAsync(store.Tenant);
        var mediator = scope.ServiceProvider.GetRequiredService<MediatR.IMediator>();

        await mediator.Send(new Application.Features.Analytics.Commands.RollUpBehaviouralEventsCommand());
        var rollups = scope.ServiceProvider.GetRequiredService<Application.Features.Analytics.Contracts.IEventRollups>();
        (await rollups.RolledUpThroughAsync(CancellationToken.None)).Should()
            .BeNull("اليوم الجاري لا يُجمَّع: تجميعُه يكتب رقماً ناقصاً ثم تمنع العلامةُ تصحيحَه");

        (await mediator.Send(new Application.Features.Analytics.Commands.PurgeBehaviouralEventsCommand()))
            .Should().Be(0, "وبلا علامةٍ لا يُمسح شيء");
    }

    private async Task<BehaviouralEvent> WaitForEventAsync(int tenantId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var found = await _api.WithDbAsync(db => db.BehaviouralEvents.IgnoreQueryFilters().AsNoTracking()
                .Where(e => e.TenantId == tenantId)
                .OrderBy(e => e.Id)
                .FirstOrDefaultAsync());
            if (found is not null) return found;
            await Task.Delay(25);
        }

        throw new InvalidOperationException("لم يُكتب حدثٌ سلوكيّ خلال عشر ثوانٍ");
    }

    // مسحٌ لا يسبق تجميعاً، على القاعدة الحقيقية: بلا علامةِ تجميع لا يُمسح صفٌّ واحد ولو تجاوز
    // مدّة الحفظ بكثير. القاعدةُ في المعالج، وهذا يثبّتها على صفوفٍ فعليّة.
    [Fact]
    public async Task المسح_لا_يمسّ_يوماً_لم_يُجمَّع()
    {
        var store = await _factory.CreateStoreAsync();

        await using var scope = await _factory.TenantScopeAsync(store.Tenant);
        var db = scope.ServiceProvider.GetRequiredService<Souq.Infrastructure.Persistence.AppDbContext>();

        var stale = BehaviouralEvent.For(
            Guid.NewGuid(), BehaviouralEventNames.ItemViewed, 1, BehaviouralSurfaces.Storefront, "ar",
            """{"productId":1,"unitPrice":5,"currency":"JOD","inStock":true}""",
            DateTime.UtcNow.AddDays(-400), DateTime.UtcNow.AddDays(-400))!;
        db.BehaviouralEvents.Add(stale);
        await db.SaveChangesAsync();

        var purged = await scope.ServiceProvider.GetRequiredService<MediatR.IMediator>()
            .Send(new Application.Features.Analytics.Commands.PurgeBehaviouralEventsCommand());

        purged.Should().Be(0, "بلا تجميعٍ لا يُمسح شيء — المجموع لا يُحتسب من صفوفٍ مُسِحت");
        (await db.BehaviouralEvents.CountAsync()).Should().Be(1);
    }
}

// أسماءُ الملفَّين كما يراهما المتصفّح. نسخةٌ في الاختبار عمداً: الأصلُ `internal` في الـ API،
// ونسخُ **الاسم** هنا يجعل تغييرَه يُسقط اختباراً بدل أن يمرّ صامتاً.
internal static class VisitorCookieNames
{
    public const string Visitor = "souq_v";
    public const string Session = "souq_s";
}
