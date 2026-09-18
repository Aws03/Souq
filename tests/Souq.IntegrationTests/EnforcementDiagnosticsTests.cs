using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Souq.Infrastructure.Persistence;
using Souq.IntegrationTests.Infrastructure;

using Microsoft.Extensions.Logging;
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

    // ========================================================================
    // السبب الثاني لنفس الأعراض (M15): السلسلة أطول من ForwardLimit.
    //
    // الفحص الأول أعلاه لا يراه إطلاقاً: nginx **يستبدل** X-Forwarded-Proto فتصل بقيمة واحدة تُستهلك
    // بنجاح ولا يبقى منها شيء — بينما **يُلحق** X-Forwarded-For، فقفزتان تعنيان قيمتين والحدّ واحد.
    // النتيجة عنوان وكيلٍ مكان عنوان كل زائر: حدّ معدّل واحد للجميع، وتدقيقٌ ينسب كل شيء إلى الحافّة.
    // ========================================================================
    [Theory]
    [InlineData("203.0.113.9", true)]                  // بقيت قيمة ⇒ استُهلك أقلّ ممّا وصل: هذا هو العطل
    [InlineData("203.0.113.9, 198.51.100.4", true)]    // بقيت قيمتان ⇒ سلسلة أطول بكثير
    [InlineData("", false)]                            // فرغت ⇒ استُهلكت كلّها: سليم
    [InlineData("   ", false)]                         // فراغٌ فقط ⇒ كأنّها فرغت
    [InlineData(null, false)]                          // لا ترويسة ⇒ لا وكيل
    public void اكتشاف_سلسلة_وكلاء_أطول_من_الحدّ_يقوم_على_بقاء_قيمة_في_الترويسة(string? remaining, bool expected)
    {
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        if (remaining is not null) context.Request.Headers["X-Forwarded-For"] = remaining;

        Souq.API.Observability.ProxyTrustDiagnostics.HasUnconsumedForwardedFor(context).Should().Be(expected);
    }

    // ========================================================================
    // خطوة الترحيل المتعمّدة (M17، R-18): الإطفاء لا يُرحّل، **ويقول إن كان المخطّط متأخّراً**.
    //
    // هذا النصف الثاني هو كلّ الفائدة. إطفاءٌ صامت يعني أنّ نشراً نُسيت خطوة ترحيله يُقلع بنجاح ثمّ
    // يفشل في أول طلب بخطأ SQL غامض عن عمودٍ غير موجود — والسبب الحقيقي بعيدٌ عن الرسالة بخطوة كاملة.
    // ========================================================================
    [Fact]
    public async Task إطفاء_ترحيل_الإقلاع_لا_يُرحّل_ويُبلّغ_عن_هجرةٍ_معلّقة()
    {
        var logger = new CapturingLoggerProvider();
        using var factory = LoggerFactory.Create(b => b.AddProvider(logger).SetMinimumLevel(LogLevel.Information));

        // القاعدة مُرحَّلة أصلاً في هذه الحزمة، فالمتوقّع سطرُ "المخطّط محدَّث" لا سطر تحذير.
        await DbSeeder.SeedAsync(_factory.Services,
            new SeedOptions(null, null, IsDevelopment: false, [], MigrateOnStartup: false),
            factory.CreateLogger("Souq.Seeding"));

        logger.Entries.Should().Contain(e => e.Message.Contains("Startup migrations are disabled"),
            "الإطفاء يُعلن عن نفسه — إطفاءٌ صامت لا يُميَّز عن ترحيلٍ جرى");
        logger.Entries.Should().NotContain(e => e.Message.Contains("Migrations applied"),
            "لا ترحيل حين يكون مُطفأً");
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
