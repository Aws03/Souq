using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Souq.Application.Common.Tenancy;
using Souq.Domain.Auditing;
using Souq.Infrastructure.Persistence;
using Souq.IntegrationTests.Infrastructure;

namespace Souq.IntegrationTests;

// ============================================================================
// ما يعتمد عليه عارض سجلّ النشاط في لوحة المنصّة، عبر HTTP الحقيقي: التصفية بالحساب وبالمدّة وبالمتجر معاً،
// والترقيم من الخادم (العدد الكلي والصفحة التالية، الأحدث أولاً)، وحدود المدّة كما يرسلها المتصفّح (لحظة UTC
// بلاحقة Z — لا تُقرأ بتوقيت الخادم)، ورفض مدّة معكوسة، ولحظات الردود بلاحقة Z. وقراءة السجلّ نفسها سطرٌ فيه.
//
// السطور تُكتب مباشرة بلحظات معروفة ومتجر ومعرّف فاعل لا يشاركهما اختبار آخر (السجلّ بلا مفتاح أجنبي إلى المتاجر
// عمداً)، فالنتيجة محدّدة مهما كتبت بقية المجموعة في السجلّ.
// ============================================================================
[Collection(IntegrationCollection.Name)]
public class PlatformAuditViewerTests
{
    private readonly SouqApiFactory _factory;
    private readonly TestApi _api;

    public PlatformAuditViewerTests(SouqApiFactory factory)
    {
        _factory = factory;
        _api = new TestApi(factory);
    }

    [Fact]
    public async Task الحساب_والمدّة_والمتجر_معاً_والترقيم_من_الخادم_الأحدث_أولاً()
    {
        var (tenantId, actorId) = Unique();
        var noon = new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc);
        await SeedAsync(
            (noon.AddMinutes(-90), tenantId, actorId, "tenant.suspended"),   // قبل المدّة
            (noon.AddMinutes(-30), tenantId, actorId, "tenant.activated"),
            (noon, tenantId, actorId, "tenant.domain.added"),
            (noon.AddMinutes(10), tenantId, actorId + 1, "tenant.updated"),   // فاعل آخر
            (noon.AddMinutes(20), tenantId, actorId, "tenant.archived"),
            (noon.AddMinutes(90), tenantId, actorId, "tenant.modules.updated"));  // بعد المدّة

        var owner = await _api.PlatformOwnerAsync();
        var window = $"tenantId={tenantId}&actorUserId={actorId}&from={Iso(noon.AddHours(-1))}&to={Iso(noon.AddHours(1))}";

        var first = await PageAsync(owner, $"{window}&page=1&pageSize=2");
        first.TotalCount.Should().Be(3);
        first.TotalPages.Should().Be(2);
        first.Items.Select(a => a.Action).Should().Equal("tenant.archived", "tenant.domain.added");

        var second = await PageAsync(owner, $"{window}&page=2&pageSize=2");
        second.Items.Select(a => a.Action).Should().Equal("tenant.activated");
        second.Items.Should().OnlyContain(a => a.TenantId == tenantId && a.ActorUserId == actorId);

        // بادئة الفعل مع بقية المرشّحات: "tenant.domain." وحدها.
        (await PageAsync(owner, $"{window}&action=tenant.domain.")).Items.Select(a => a.Action).Should().Equal("tenant.domain.added");
    }

    [Fact]
    public async Task حدود_المدّة_لحظات_UTC_كما_يرسلها_المتصفّح_وشاملة()
    {
        var (tenantId, actorId) = Unique();
        var at = new DateTime(2026, 4, 2, 21, 30, 0, DateTimeKind.Utc);
        await SeedAsync((at, tenantId, actorId, "tenant.created"));
        var owner = await _api.PlatformOwnerAsync();

        // الحدّان شاملان، ولاحقة Z تُقرأ UTC: على خادم بتوقيت غير UTC كانت ستنزاح المدّة بفرق توقيته.
        // (action=tenant.: كل قراءة هنا تكتب سطر platform.audit.viewed لهذا المتجر، بلحظة "الآن".)
        var scope = $"tenantId={tenantId}&action=tenant.";
        (await PageAsync(owner, $"{scope}&from={Iso(at)}&to={Iso(at)}")).TotalCount.Should().Be(1);
        (await PageAsync(owner, $"{scope}&from={Iso(at.AddMilliseconds(1))}")).TotalCount.Should().Be(0);
        (await PageAsync(owner, $"{scope}&to={Iso(at.AddMilliseconds(-1))}")).TotalCount.Should().Be(0);

        // والإزاحة الصريحة لحظةٌ واحدة أيضاً: 00:30 بتوقيت +03:00 هي 21:30 UTC.
        var offset = Uri.EscapeDataString("2026-04-03T00:30:00+03:00");
        (await PageAsync(owner, $"{scope}&from={offset}&to={offset}")).TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task مدّة_معكوسة_وصفحة_أكبر_من_الحدّ_تُرفضان_والقراءة_نفسها_تُسجَّل()
    {
        var owner = await _api.PlatformOwnerAsync();

        var reversed = await owner.GetAsync($"/api/platform/audit?from={Iso(new DateTime(2026, 5, 2, 0, 0, 0, DateTimeKind.Utc))}" +
                                            $"&to={Iso(new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc))}");
        reversed.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await owner.GetAsync("/api/platform/audit?pageSize=100000")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var (tenantId, _) = Unique();
        await PageAsync(owner, $"tenantId={tenantId}");
        var ownerId = (await owner.GetFromJsonAsync<TestApi.UserBody>("/api/auth/me", TestApi.Json))!.Id;

        var reads = await PageAsync(owner, $"tenantId={tenantId}&action=platform.audit.viewed&actorUserId={ownerId}");
        reads.Items.Should().NotBeEmpty();
        reads.Items.Should().OnlyContain(a => a.Area == "Platform" && a.ActorRole == "PlatformOwner");
    }

    [Fact]
    public async Task السجلّ_على_مضيف_المنصّة_وحده_ولحساب_منصّة_وحده()
    {
        (await _api.Anonymous().GetAsync("/api/platform/audit")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var storeAdmin = await _api.AdminAsync();
        (await storeAdmin.GetAsync("/api/platform/audit")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await _api.Client(SouqApiFactory.PlatformHost).GetAsync("/api/platform/audit")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task لحظات_الردود_UTC_بلاحقة_Z_لا_نصّاً_يقرؤه_المتصفّح_بتوقيته()
    {
        var (tenantId, actorId) = Unique();
        await SeedAsync((new DateTime(2026, 6, 1, 8, 15, 30, DateTimeKind.Utc), tenantId, actorId, "tenant.created"));
        var owner = await _api.PlatformOwnerAsync();

        // ما يعود من قاعدة البيانات بلا Kind: سطر التدقيق وآخر دخول حساب.
        using var audit = JsonDocument.Parse(await owner.GetStringAsync($"/api/platform/audit?tenantId={tenantId}&action=tenant."));
        audit.RootElement.GetProperty("items")[0].GetProperty("occurredAt").GetString().Should().Be("2026-06-01T08:15:30Z");

        using var users = JsonDocument.Parse(await owner.GetStringAsync("/api/platform/users?pageSize=100"));
        var owners = users.RootElement.GetProperty("items").EnumerateArray().Where(u => u.GetProperty("lastLoginAt").ValueKind == JsonValueKind.String);
        owners.Should().NotBeEmpty();
        owners.Should().OnlyContain(u => u.GetProperty("lastLoginAt").GetString()!.EndsWith('Z')
                                          && u.GetProperty("createdAt").GetString()!.EndsWith('Z'));
    }

    private static (int TenantId, int ActorId) Unique()
    {
        // خارج مدى المعرّفات الحقيقية في قاعدة الاختبار: لا متجر ولا حساب بهذين الرقمين.
        var n = Random.Shared.Next(1_000_000, 2_000_000_000);
        return (n, n);
    }

    private static string Iso(DateTime utc) => Uri.EscapeDataString(utc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));

    private async Task SeedAsync(params (DateTime At, int TenantId, int ActorId, string Action)[] rows)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().UsePlatform();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var row in rows)
            db.AuditEntries.Add(new AuditEntry(row.At, AuditAreas.Platform, row.Action, row.TenantId, row.ActorId,
                "PlatformOwner", "Tenant", row.TenantId.ToString(), null, null, null));
        await db.SaveChangesAsync();
    }

    private static async Task<TestApi.PageBody<AuditRow>> PageAsync(HttpClient client, string query) =>
        (await client.GetFromJsonAsync<TestApi.PageBody<AuditRow>>($"/api/platform/audit?{query}", TestApi.Json))!;

    internal sealed record AuditRow(long Id, DateTime OccurredAt, string Area, string Action, int? TenantId, int? ActorUserId, string? ActorRole);
}
