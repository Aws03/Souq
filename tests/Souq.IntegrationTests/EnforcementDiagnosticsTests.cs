using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Souq.Infrastructure.Persistence;
using Souq.IntegrationTests.Infrastructure;

namespace Souq.IntegrationTests;

// ============================================================================
// الضوابط التي تحوّل "آليّة جاهزة" إلى "خطأ إعداد مسموع".
//
// كلاهما يعالج العطل نفسه في شكلين: إعداد إنتاج خاطئ لا يُنتج أي إشارة. وكيل غير موثوق يكسر
// HSTS وروابط البريد وحدّ المعدّل بصمت؛ وهوية قاعدة أقوى من اللازم لا تظهر في أي مكان.
// ============================================================================
[Collection(IntegrationCollection.Name)]
public class EnforcementDiagnosticsTests
{
    private readonly SouqApiFactory _factory;
    private readonly TestApi _api;

    public EnforcementDiagnosticsTests(SouqApiFactory factory)
    {
        _factory = factory;
        _api = new TestApi(factory);
    }

    [Theory]
    [InlineData("https", false, true)]    // ترويسة باقية + مخطّط http ⇒ لم تُصدَّق: هذا هو العطل
    [InlineData("https", true, false)]    // المخطّط https أصلاً ⇒ صُدِّقت أو لا وكيل
    [InlineData("http", false, false)]    // وكيل يعلن http ⇒ لا شيء يُكتشف
    [InlineData(null, false, false)]      // لا ترويسة ⇒ لا وكيل
    public void اكتشاف_وكيل_غير_موثوق_يقوم_على_بقاء_الترويسة_بعد_الوسيط(string? forwarded, bool isHttps, bool expected)
    {
        // وسيط الإطار *يحذف* ما استهلكه، فبقاء الترويسة مع مخطّط http دليل قاطع على عدم التصديق.
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Request.Scheme = isHttps ? "https" : "http";
        if (forwarded is not null) context.Request.Headers["X-Forwarded-Proto"] = forwarded;

        Souq.API.Observability.ProxyTrustDiagnostics.WasForwardedProtoIgnored(context).Should().Be(expected);
    }

    [Fact]
    public async Task فحص_صلاحيات_القاعدة_يقرأ_الهوية_الحقيقية_من_القاعدة()
    {
        // حاوية الاختبارات تتصل بـ sa — وهو بالضبط ما يجب أن يكشفه الفحص في نشر حقيقي.
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var report = await DatabasePrivileges.InspectAsync(db);

        report.Should().NotBeNull("الفحص يجب أن يعمل على SQL Server حقيقي");
        report!.Login.Should().NotBeNullOrEmpty();
        report.CanChangeSchema.Should().BeTrue("الاتصال هنا بـ sa، فالفحص يجب أن يرى صلاحية تغيير المخطّط");
        report.Roles.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("Server=db;Database=S;User Id=souq_app;Password=x", "Server=db;Database=S;User Id=souq_app;Password=y", true)]
    [InlineData("Server=db;Database=S;User Id=souq_app;Password=x", "Server=db;Database=S;User Id=souq_migrator;Password=y", false)]
    public void فصل_هويتي_التشغيل_والهجرات_يُقاس_بالهوية_لا_بوجود_المفتاح(string runtime, string migrations, bool same)
    {
        // ضبط ConnectionStrings:Migrations بنسخة من Default يبدو فصلاً في الإعداد وليس فصلاً في القاعدة.
        DatabasePrivileges.SameLogin(runtime, migrations).Should().Be(same);
    }
}
