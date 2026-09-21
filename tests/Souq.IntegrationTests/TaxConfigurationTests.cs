using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Souq.IntegrationTests.Infrastructure;

namespace Souq.IntegrationTests;

// ============================================================================
// إعدادُ الضريبة عبر الخادم الحقيقي (ADR-0055، قرار المالك P-06).
//
// **وما يُثبَت هنا أوّلاً هو الحدّ**: إصدارٌ منشورٌ لم يتحقّق منه أحد **لا يسمح بالجمع**. هذا هو
// موضعُ الفشل الوحيد الذي يهمّ فعلاً: نسبةٌ صحيحةُ الشكل، منشورةٌ، مختارةٌ لمتجر، والتاجر فعّل
// الجمع — ومع ذلك لا تُجمَع، لأنّ أحداً لم يؤكّدها. فلو انقلبت هذه القاعدة يوماً لَصار رقمٌ وجدته
// الهندسة في وثيقةٍ يُفرَض على مشترٍ حقيقيّ.
//
// ولا يُبذَر في هذه الاختبارات ملفٌّ لأيّ اختصاص حقيقيّ بقيمٍ حقيقية: القيمُ أدناه **صناعيّة
// للاختبار**، لا مرجعٌ لقاعدةٍ أردنية ولا غيرها.
// ============================================================================
[Collection(IntegrationCollection.Name)]
public class TaxConfigurationTests
{
    private readonly SouqApiFactory _factory;
    private readonly TestApi _api;

    public TaxConfigurationTests(SouqApiFactory factory)
    {
        _factory = factory;
        _api = new TestApi(factory);
    }

    private static readonly JsonSerializerOptions Json = TestApi.Json;

    [Fact]
    public async Task ملفّ_الاختصاص_يُنشأ_ويُنشر_ولا_يسمح_بالجمع_حتى_يتحقّق_منه_مهنيّ()
    {
        var owner = await _api.PlatformOwnerAsync();
        var jurisdiction = $"T{Random.Shared.Next(100, 999)}";

        // (1) ملفّ الاختصاص.
        var created = await owner.PostAsJsonAsync("/api/platform/tax/profiles",
            new { jurisdiction, name = "Test jurisdiction" });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var profileId = (await created.Content.ReadFromJsonAsync<IdResponse>(Json))!.Id;

        // وملفٌّ ثانٍ للاختصاص نفسه مرفوض: التصحيحُ إصدارٌ لا ملفّ.
        (await owner.PostAsJsonAsync("/api/platform/tax/profiles", new { jurisdiction, name = "Duplicate" }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        // (2) مسوّدةُ إصدارٍ بقيمٍ **صناعيّة**، ثم نشرُها.
        var version = await owner.PostAsJsonAsync($"/api/platform/tax/profiles/{profileId}/versions", new
        {
            effectiveFrom = DateTime.UtcNow.AddDays(-1),
            priceMode = "Inclusive",
            shippingTaxable = true,
            rates = new[] { new { code = "standard", name = "Standard (test)", basisPoints = 1000, category = (string?)null } },
            notes = "قيمٌ اختبارية لا مرجعَ لها",
        });
        version.StatusCode.Should().Be(HttpStatusCode.Created);
        var versionId = (await version.Content.ReadFromJsonAsync<IdResponse>(Json))!.Id;

        (await owner.PostAsync($"/api/platform/tax/profiles/{profileId}/versions/{versionId}/publish", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        // (3) متجرٌ يختار الملفّ ويفعّل الجمع — **ولا يُجمَع**، والسببُ يُقال له بالاسم.
        var store = await _factory.CreateStoreAsync();
        var admin = await _api.ForStore(store).AdminAsync();

        var settings = await admin.PutAsJsonAsync("/api/admin/store/tax",
            new { taxProfileId = profileId, collectionEnabled = true, registrationNumber = "TEST-1" });
        settings.StatusCode.Should().Be(HttpStatusCode.OK);

        var afterSelect = await settings.Content.ReadFromJsonAsync<StoreTaxView>(Json);
        afterSelect!.Collecting.Should().BeFalse(
            "إصدارٌ منشورٌ لم يتحقّق منه أحد لا يُجمَع به — وهذا هو الحدّ الذي يوجد لأجله كلُّ ما سبق");
        afterSelect.Reason.Should().Be("VersionNotVerified");
        afterSelect.EffectiveVersion!.VerificationState.Should().Be("Unverified");

        // (4) تسجيلُ تحقّقٍ مهنيّ **باسمه** — وحينها وحدها يُجمَع.
        (await owner.PostAsJsonAsync(
                $"/api/platform/tax/profiles/{profileId}/versions/{versionId}/verify",
                new { verifiedBy = "مكتب محاسبة (اختبار)", note = "تحقّقٌ اختباري" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterVerify = await (await admin.GetAsync("/api/admin/store/tax"))
            .Content.ReadFromJsonAsync<StoreTaxView>(Json);
        afterVerify!.Collecting.Should().BeTrue();
        afterVerify.Reason.Should().Be("Collecting");
        afterVerify.EffectiveVersion!.VerifiedBy.Should().Be("مكتب محاسبة (اختبار)");

        // (5) وسحبُ التحقّق يُوقف الجمع فوراً.
        (await owner.PostAsJsonAsync(
                $"/api/platform/tax/profiles/{profileId}/versions/{versionId}/require-confirmation",
                new { note = "مراجعةٌ جديدة" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterWithdrawal = await (await admin.GetAsync("/api/admin/store/tax"))
            .Content.ReadFromJsonAsync<StoreTaxView>(Json);
        afterWithdrawal!.Collecting.Should().BeFalse();
        afterWithdrawal.Reason.Should().Be("VersionNotVerified");
    }

    // كلُّ حالةٍ لا تُجمَع فيها ضريبةٌ تقول سببَها بالاسم: صفرٌ بلا سببٍ يقرأ كأنه عطب.
    [Fact]
    public async Task المتجر_يقرأ_سبب_عدم_الجمع_بالاسم()
    {
        var store = await _factory.CreateStoreAsync();
        var admin = await _api.ForStore(store).AdminAsync();

        // (أ) لم يختر ملفّاً — وهو حال كل متجر قائم يوم شُحنت هذه القدرة.
        var initial = await (await admin.GetAsync("/api/admin/store/tax"))
            .Content.ReadFromJsonAsync<StoreTaxView>(Json);
        initial!.Collecting.Should().BeFalse();
        initial.Reason.Should().Be("NoProfileSelected");
        initial.TaxProfileId.Should().BeNull();

        // (ب) ملفٌّ بلا إصدارٍ نافذ.
        var owner = await _api.PlatformOwnerAsync();
        var created = await owner.PostAsJsonAsync("/api/platform/tax/profiles",
            new { jurisdiction = $"E{Random.Shared.Next(100, 999)}", name = "Empty (test)" });
        var profileId = (await created.Content.ReadFromJsonAsync<IdResponse>(Json))!.Id;

        var selected = await (await admin.PutAsJsonAsync("/api/admin/store/tax",
                new { taxProfileId = profileId, collectionEnabled = true, registrationNumber = (string?)null }))
            .Content.ReadFromJsonAsync<StoreTaxView>(Json);
        selected!.Reason.Should().Be("NoEffectiveVersion");

        // (ج) اختار ولم يفعّل.
        var disabled = await (await admin.PutAsJsonAsync("/api/admin/store/tax",
                new { taxProfileId = profileId, collectionEnabled = false, registrationNumber = (string?)null }))
            .Content.ReadFromJsonAsync<StoreTaxView>(Json);
        disabled!.Reason.Should().Be("CollectionDisabled");
    }

    // ملفٌّ لا وجود له يُرفض: اختيارٌ صامتٌ لمعرّفٍ خاطئ يترك التاجر يظنّ أنه ضبط شيئاً.
    [Fact]
    public async Task اختيار_ملفّ_لا_وجود_له_يُرفض()
    {
        var store = await _factory.CreateStoreAsync();
        var admin = await _api.ForStore(store).AdminAsync();

        (await admin.PutAsJsonAsync("/api/admin/store/tax",
                new { taxProfileId = 999_999, collectionEnabled = false, registrationNumber = (string?)null }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // نقاطُ المنصّة على مضيف المنصّة وحده، وبصلاحيتها: نقطةُ إعدادٍ ضريبيّ ليست للتاجر.
    [Fact]
    public async Task نقاط_ملفّات_الضريبة_للمنصّة_وحدها()
    {
        var store = await _factory.CreateStoreAsync();
        var api = _api.ForStore(store);

        // على مضيف المتجر: النقطة غير موجودة (404، لا نكشف وجود منطقة المنصّة).
        (await (await api.AdminAsync()).GetAsync("/api/platform/tax/profiles"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        // والتاجر يرى ما يستطيع اختياره من نقطته هو.
        (await (await api.AdminAsync()).GetAsync("/api/admin/store/tax/profiles"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private sealed record IdResponse(int Id);

    private sealed record StoreTaxView(
        int? TaxProfileId, string? Jurisdiction, string? ProfileName, bool CollectionEnabled,
        string? RegistrationNumber, DateTime? SelectedAt, VersionView? EffectiveVersion, bool Collecting, string Reason);

    private sealed record VersionView(
        int Id, int Version, DateTime EffectiveFrom, string Status, string PriceMode, bool ShippingTaxable,
        string VerificationState, string? VerifiedBy, DateTime? VerifiedAt);
}
