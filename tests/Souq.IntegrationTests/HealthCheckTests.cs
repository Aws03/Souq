using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Souq.API.Observability;
using Souq.Application.Common.Tenancy;
using Souq.Infrastructure.Persistence;
using Souq.IntegrationTests.Infrastructure;

namespace Souq.IntegrationTests;

// ============================================================================
// المسباران يفترقان عمداً (Observability/Health.cs): الحيوية بلا تبعية، والجاهزية تلمس القاعدة.
// ما يثبَّت هنا:
//   • الحيوية تجيب على مضيف لا متجر عليه — وهي حال مسبار المنظّم دائماً. لو انزلقت النقطة
//     يوماً تحت /api لردّ عليها TenantResolutionMiddleware بـ 404 وبقيت الحاوية "غير سليمة"
//     في الإنتاج وحده. الاختبار يقابل المسارين على المضيف نفسه.
//   • الجاهزية ترفض مخطّطاً أقدم من الإصدار (استعادة نسخة احتياطية أقدم — R-19) وقاعدة متعذّرة.
//   • الجسم لا يكشف سبباً: النقطة مجهولة الهوية.
// ============================================================================
[Collection(IntegrationCollection.Name)]
public class HealthCheckTests
{
    private readonly SouqApiFactory _factory;
    private readonly TestApi _api;

    public HealthCheckTests(SouqApiFactory factory)
    {
        _factory = factory;
        _api = new TestApi(factory);
    }

    [Fact]
    public async Task الحيوية_تجيب_على_مضيف_لا_متجر_عليه_بخلاف_نقاط_api()
    {
        var unknownHost = _api.Client("no-store-here.invalid");

        var live = await unknownHost.GetAsync(HealthEndpoints.Live);
        var api = await unknownHost.GetAsync("/api/storefront/config");

        live.StatusCode.Should().Be(HttpStatusCode.OK, "مسبار المنظّم يصل بمضيف الحاوية لا بمضيف متجر");
        api.StatusCode.Should().Be(HttpStatusCode.NotFound, "وتحديد المستأجر ما زال يحرس /api على المضيف نفسه");
    }

    [Fact]
    public async Task الحيوية_لا_تشغّل_أي_فحص_ولا_تلمس_القاعدة()
    {
        var body = await (await _api.Anonymous().GetAsync(HealthEndpoints.Live)).Content.ReadFromJsonAsync<HealthBody>(TestApi.Json);

        body!.Status.Should().Be(nameof(HealthStatus.Healthy));
        body.Checks.Should().BeEmpty();
    }

    [Fact]
    public async Task الجاهزية_تنجح_على_قاعدة_مُرحَّلة_وتذكر_فحص_القاعدة()
    {
        _api.Anonymous();   // يُقلع الخادم (الهجرات + البذر)

        var response = await _api.Anonymous().GetAsync(HealthEndpoints.Ready);
        var body = await response.Content.ReadFromJsonAsync<HealthBody>(TestApi.Json);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body!.Status.Should().Be(nameof(HealthStatus.Healthy));
        body.Checks.Should().ContainKey("database").WhoseValue.Should().Be(nameof(HealthStatus.Healthy));
    }

    [Fact]
    public async Task الجسم_لا_يكشف_سبباً_ولا_مدّة_تنفيذ()
    {
        var raw = await (await _api.Anonymous().GetAsync(HealthEndpoints.Ready)).Content.ReadAsStringAsync();

        // النقطة مجهولة الهوية: الحالة واسم الفحص فقط — لا وصف ولا استثناء ولا duration.
        raw.ToLowerInvariant().Should().NotContainAny("description", "exception", "duration", "stack", "connectionstring");
    }

    [Fact]
    public async Task الجاهزية_تفشل_على_قاعدة_بمخطّط_أقدم_من_الإصدار()
    {
        // سيناريو R-19 بالضبط: استُعيدت نسخة احتياطية أقدم من الإصدار المنشور. القاعدة تستجيب،
        // لكن هجراتها ناقصة — خدمة الطلبات على مخطّط مجهول أخطر من رفضها بوضوح.
        var database = $"health_{Guid.NewGuid():N}"[..24];
        await ExecuteOnMasterAsync($"CREATE DATABASE [{database}]");
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(
            new SqlConnectionStringBuilder(_factory.ConnectionString) { InitialCatalog = database }.ConnectionString).Options;

        await using var db = new AppDbContext(options, new TenantContext());
        try
        {
            (await db.Database.CanConnectAsync()).Should().BeTrue("القاعدة نفسها تستجيب — الخلل في المخطّط لا في الاتصال");

            var result = await CheckAsync(db);

            result.Status.Should().Be(HealthStatus.Unhealthy);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task الجاهزية_تفشل_حين_تتعذّر_القاعدة()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=127.0.0.1,1;Database=none;User Id=sa;Password=no;TrustServerCertificate=True;Connect Timeout=1")
            .Options;

        await using var db = new AppDbContext(options, new TenantContext());

        (await CheckAsync(db)).Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task مسبار_الحاوية_يميّز_الجاهز_من_غير_المتاح()
    {
        // ما ينفّذه HEALTHCHECK في src/Souq.API/Dockerfile: رمز خروج لا نص.
        (await HealthProbe.ProbeAsync(_api.Anonymous(), HealthEndpoints.Ready)).Should().Be(0);

        using var nothingListening = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        (await HealthProbe.ProbeAsync(nothingListening, $"http://127.0.0.1:1{HealthEndpoints.Ready}")).Should().Be(1);
    }

    private static Task<HealthCheckResult> CheckAsync(AppDbContext db) =>
        new DatabaseHealthCheck(db, NullLogger<DatabaseHealthCheck>.Instance)
            .CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

    private async Task ExecuteOnMasterAsync(string sql)
    {
        var master = new SqlConnectionStringBuilder(_factory.ConnectionString) { InitialCatalog = "master" }.ConnectionString;
        await using var connection = new SqlConnection(master);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
