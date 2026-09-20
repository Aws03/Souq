using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Souq.Domain.Platform;
using Souq.Infrastructure.Persistence;
using Souq.IntegrationTests.Infrastructure;

namespace Souq.IntegrationTests;

// ============================================================================
// مستوى التحكّم التجاري عبر HTTP الحقيقي (C1، ADR-0047/0053). ما يثبته هنا ولا يثبته اختبار وحدة:
// أن **الجواب الواحد** هو الذي يفرضه الوسيط فعلاً — فالخطة تُلغى، والدليل يُبطَل، والنقطة نفسها
// تجيب 404 ModuleDisabled — وأن ما تعلنه نقطة إعداد الواجهة هو الجواب نفسه لا العمود الخام.
// ============================================================================
[Collection(IntegrationCollection.Name)]
public class CommercialControlPlaneTests
{
    private readonly SouqApiFactory _factory;
    private readonly TestApi _api;

    public CommercialControlPlaneTests(SouqApiFactory factory)
    {
        _factory = factory;
        _api = new TestApi(factory);
    }

    [Fact]
    public async Task خطّة_غير_قابلة_للحلّ_لا_تمنح_شيئاً()
    {
        // متجر جديد يبدأ على الخطة التأسيسية، فكوبوناته تعمل…
        var owner = await _api.PlatformOwnerAsync();
        var store = await _factory.CreateStoreAsync();
        var storeApi = _api.ForStore(store);
        (await storeApi.Anonymous().GetAsync("/api/coupons")).StatusCode
            .Should().NotBe(HttpStatusCode.NotFound, "الخطة التأسيسية تمنح الكوبونات");

        // …وإلغاء الاشتراك يترك المتجر بلا عقد: لا استحقاق واحد، ولا شيء يُمنح بالغياب.
        (await owner.DeleteAsync($"/api/platform/tenants/{store.Tenant.Id}/plan"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await ProblemAsync(await storeApi.Anonymous().GetAsync("/api/coupons")))
            .Should().Be((HttpStatusCode.NotFound, "ModuleDisabled"));

        // والعمود الخام لم يتغيّر: الإطفاء جاء من العقد لا من مفتاح المنصّة — وهذا هو التقاطع.
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Tenants.AsNoTracking()
            .Where(t => t.Id == store.Tenant.Id)
            .Select(t => EF.Property<string>(t, "_modules"))
            .FirstAsync();
        stored.Should().Contain(StoreModules.Promotions);
    }

    [Fact]
    public async Task إعداد_الواجهة_يعلن_الوحدات_الفعّالة_لا_العمود_الخام()
    {
        // لو أعلنت هذه النقطة العمود لعرضت الواجهة وحدةً يردّ عليها الخادم 404.
        var owner = await _api.PlatformOwnerAsync();
        var store = await _factory.CreateStoreAsync();
        var storeApi = _api.ForStore(store);

        (await owner.DeleteAsync($"/api/platform/tenants/{store.Tenant.Id}/plan"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var config = await storeApi.Anonymous().GetFromJsonAsync<JsonElement>("/api/storefront/config");
        config.GetProperty("modules").EnumerateArray().Should().BeEmpty();
    }

    [Fact]
    public async Task إسناد_خطة_يعيد_الوحدة_فوراً_ويظهر_مصدرها_في_شاشة_المنصّة()
    {
        var owner = await _api.PlatformOwnerAsync();
        var store = await _factory.CreateStoreAsync();
        var storeApi = _api.ForStore(store);

        (await owner.DeleteAsync($"/api/platform/tenants/{store.Tenant.Id}/plan"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var foundationId = await FoundationPlanIdAsync();
        (await owner.PutAsJsonAsync($"/api/platform/tenants/{store.Tenant.Id}/plan", new { planId = foundationId }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await storeApi.Anonymous().GetAsync("/api/coupons")).StatusCode.Should().NotBe(HttpStatusCode.NotFound);

        // الشاشة تعرض الجواب ومدخليه: لماذا هذه الوحدة مفعّلة؟
        var view = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/platform/tenants/{store.Tenant.Id}/entitlements");
        view.GetProperty("planCode").GetString().Should().Be(Plan.FoundationCode);
        Names(view, "planEntitlements").Should().Contain(StoreModules.Promotions);
        Names(view, "effective").Should().Contain(StoreModules.Promotions);
    }

    [Fact]
    public async Task استثناء_الدعم_ينتهي_بنفسه_ويُسحب_فوراً()
    {
        var owner = await _api.PlatformOwnerAsync();
        var store = await _factory.CreateStoreAsync();

        var granted = await owner.PostAsJsonAsync(
            $"/api/platform/tenants/{store.Tenant.Id}/entitlement-overrides",
            new { entitlement = StoreModules.Reviews, days = 7, reason = "تجربة مدفوعة للعميل" });
        granted.StatusCode.Should().Be(HttpStatusCode.Created);
        var overrideId = (await granted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        var listed = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/platform/tenants/{store.Tenant.Id}/entitlement-overrides");
        listed.EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("isActive").GetBoolean().Should().BeTrue();

        // استثناء ثانٍ سارٍ على الاستحقاق نفسه يُرفض: "متى ينتهي هذا؟" يبقى له جواب واحد.
        (await ProblemAsync(await owner.PostAsJsonAsync(
                $"/api/platform/tenants/{store.Tenant.Id}/entitlement-overrides",
                new { entitlement = StoreModules.Reviews, days = 3, reason = "سبب آخر" })))
            .Should().Be((HttpStatusCode.Conflict, "OverrideAlreadyActive"));

        (await owner.DeleteAsync(
                $"/api/platform/tenants/{store.Tenant.Id}/entitlement-overrides/{overrideId}"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task استثناء_متجر_آخر_لا_يُسحب_من_صفحة_هذا_المتجر()
    {
        // الجدول بلا مرشّح مستأجر: الشرط يكتبه المعالج بيده، وهذا ما يثبت أنه كتبه.
        var owner = await _api.PlatformOwnerAsync();
        var mine = await _factory.CreateStoreAsync();
        var theirs = await _factory.CreateStoreAsync();

        var granted = await owner.PostAsJsonAsync(
            $"/api/platform/tenants/{theirs.Tenant.Id}/entitlement-overrides",
            new { entitlement = StoreModules.Reviews, days = 7, reason = "استثناء متجر آخر" });
        var overrideId = (await granted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        (await ProblemAsync(await owner.DeleteAsync(
                $"/api/platform/tenants/{mine.Tenant.Id}/entitlement-overrides/{overrideId}")))
            .Should().Be((HttpStatusCode.NotFound, "NotFound"));
    }

    [Fact]
    public async Task الخطة_المنشورة_لا_تُعدَّل_والمسوّدة_لا_تُسنَد()
    {
        var owner = await _api.PlatformOwnerAsync();
        var code = $"tier{Guid.NewGuid():N}"[..12];

        var created = await owner.PostAsJsonAsync("/api/platform/plans", new
        {
            code, name = "شريحة اختبار", entitlements = new[] { StoreModules.Reviews },
            limits = new[] { new { name = "products.max", value = 500 } },
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var planId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        // المسوّدة لا يُشترَك عليها.
        var store = await _factory.CreateStoreAsync();
        (await ProblemAsync(await owner.PutAsJsonAsync(
                $"/api/platform/tenants/{store.Tenant.Id}/plan", new { planId })))
            .Should().Be((HttpStatusCode.UnprocessableEntity, "InvalidSubscription"));

        (await owner.PostAsync($"/api/platform/plans/{planId}/publish", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        // وبعد النشر تُجمَّد شروطها: التعديل إصدارٌ جديد لا تحرير للقائم.
        (await ProblemAsync(await owner.PutAsJsonAsync($"/api/platform/plans/{planId}", new
            {
                name = "اسم آخر", entitlements = new[] { StoreModules.Wishlist },
                limits = Array.Empty<object>(),
            })))
            .Should().Be((HttpStatusCode.UnprocessableEntity, "InvalidPlan"));
    }

    [Fact]
    public async Task نقاط_التحكّم_التجاري_على_مضيف_المنصّة_وحده_وبصلاحيتها()
    {
        var store = await _factory.CreateStoreAsync();
        var storeApi = _api.ForStore(store);

        // على مضيف متجر: غير موجودة أصلاً (لا نكشف وجودها).
        (await storeApi.Anonymous().GetAsync("/api/platform/plans"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        // على مضيف المنصّة بلا توكن: 401 لا 404 — الوجود معلوم، والمفقود هو الهوية.
        // (Anonymous() عميلٌ على مضيف **متجر**؛ مضيف المنصّة يُطلب صراحةً.)
        (await _api.Client(SouqApiFactory.PlatformHost).GetAsync("/api/platform/plans"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // وأنّ مدير المنصّة (لا مالكها) يُمنع منها يثبته مسحُ التدرّج في AuthorizationBoundaryTests،
        // وهو يشتقّ توقّعه من RolePermissions فيغطّي هذه النقاط لحظةَ توجيهها — فلا نكرّره هنا بيدنا.
        (await _api.PlatformOwnerAsync()).GetAsync("/api/platform/plans").Result
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<int> FoundationPlanIdAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Plans.AsNoTracking()
            .Where(p => p.Code == Plan.FoundationCode && p.Status == PlanStatus.Published)
            .OrderByDescending(p => p.Version)
            .Select(p => p.Id)
            .FirstAsync();
    }

    private static IEnumerable<string?> Names(JsonElement view, string property) =>
        view.GetProperty(property).EnumerateArray().Select(e => e.GetString());

    private static async Task<(HttpStatusCode, string?)> ProblemAsync(HttpResponseMessage response) =>
        (response.StatusCode, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
}
