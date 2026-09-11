using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Notifications;
using Souq.Domain.Identity;
using Souq.Infrastructure.Persistence;
using Souq.Infrastructure.Services;
using Souq.IntegrationTests.Infrastructure;

namespace Souq.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public class StartupAndSecurityTests
{
    private readonly SouqApiFactory _factory;
    private readonly TestApi _api;

    public StartupAndSecurityTests(SouqApiFactory factory)
    {
        _factory = factory;
        _api = new TestApi(factory);
    }

    [Fact]
    public async Task كل_الهجرات_تُطبَّق_على_قاعدة_جديدة_بلا_معلّق()
    {
        _api.Anonymous(); // يُقلع الخادم (الهجرات + البذر)
        var (applied, pending) = await _api.WithDbAsync(async db =>
            (await db.Database.GetAppliedMigrationsAsync(), await db.Database.GetPendingMigrationsAsync()));

        applied.Should().Contain(m => m.EndsWith("_Phase1AIntegrityPrecisionConcurrency"));
        applied.Should().Contain(m => m.EndsWith("_Phase2MultiTenancy"));
        applied.Should().Contain(m => m.EndsWith("_Phase3Identity"));
        applied.Should().Contain(m => m.EndsWith("_Phase4PlatformAdministration"));
        pending.Should().BeEmpty();
    }

    [Fact]
    public async Task خارج_Development_لا_يوجد_مدير_افتراضي_بكلمة_مرور_منشورة()
    {
        // Phase 0 B1: admin@souq.com / Admin@123 كان يُبذَر في كل البيئات بما فيها Production.
        var response = await _api.Anonymous().PostAsJsonAsync("/api/auth/login",
            new { email = DbSeeder.DevelopmentAdminEmail, password = DbSeeder.DevelopmentAdminPassword });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task المدير_يُنشأ_من_الإعداد_الصريح_ويستطيع_الدخول()
    {
        var admin = await _api.AdminAsync();

        (await admin.GetAsync("/api/admin/inventory")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task كلمة_مرور_مدير_ضعيفة_خارج_التطوير_تُفشل_الإقلاع_صراحةً()
    {
        _api.Anonymous();

        var act = () => DbSeeder.SeedAsync(_factory.Services,
            new SeedOptions("weak-admin@souq.test", "short", IsDevelopment: false, DefaultTenantHosts: []),
            NullLogger.Instance);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await _api.WithDbAsync(db => db.Users.AnyAsync(u => u.Email == "weak-admin@souq.test"))).Should().BeFalse();
    }

    [Fact]
    public async Task إعادة_التعيين_تخزّن_التجزئة_فقط_ولا_يظهر_الرمز_في_السجل_ويُستخدم_مرة_واحدة()
    {
        var (_, email) = await _api.NewCustomerAsync();
        var anonymous = _api.Anonymous();

        (await anonymous.PostAsJsonAsync("/api/auth/forgot-password", new { email })).EnsureSuccessStatusCode();
        await _factory.DispatchNotificationsAsync();   // المرحلة 14: الرمز يُولَّد ويُرسل من صندوق الصادر
        var token = _factory.Emails.LastResetTokenFor(email);

        var storedHash = await _api.WithDbAsync(db =>
            db.Users.Where(u => u.Email == email).Select(u => u.PasswordResetTokenHash).SingleAsync());
        storedHash.Should().Be(User.HashToken(token)).And.NotBe(token);                   // B6
        _factory.Logs.Messages.Should().NotContain(m => m.Contains(token));               // B2

        var reset = await anonymous.PostAsJsonAsync("/api/auth/reset-password", new { token, newPassword = "Brand-New-Pass-9" });
        reset.StatusCode.Should().Be(HttpStatusCode.OK);
        (await _api.LoginAsync(email, "Brand-New-Pass-9")).Should().NotBeNull();

        var reuse = await anonymous.PostAsJsonAsync("/api/auth/reset-password", new { token, newPassword = "Another-Pass-10" });
        reuse.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);                  // استخدام واحد
        (await reuse.Content.ReadFromJsonAsync<TestApi.ProblemBody>(TestApi.Json))!.Code.Should().Be("InvalidResetToken");
    }

    [Fact]
    public async Task بديل_البريد_الطرفي_لا_يطبع_رابط_إعادة_التعيين_خارج_التطوير()
    {
        // Phase 0 B2: كان الرابط (سرّ حامله) يُطبَع كاملاً في السجل في كل البيئات.
        var logs = new CapturingLoggerProvider();
        var service = new ConsoleEmailService(
            Options.Create(new ConsoleEmailOptions { IncludeLinksInLog = false }),
            logs.CreateLogger("test") is var inner ? new LoggerAdapter(inner) : null!);

        const string link = "https://store.test/reset-password?token=secret-reset-token";
        await service.SendAsync(new EmailMessage("victim@souq.test", "إعادة تعيين كلمة المرور", $"<a href=\"{link}\">x</a>", link,
            "متجر", null, "PasswordReset", link), CancellationToken.None);

        logs.Messages.Should().NotBeEmpty();
        logs.Messages.Should().NotContain(m => m.Contains("secret-reset-token") || m.Contains("victim@souq.test"));
    }

    [Theory]
    [InlineData("/api/products?page=0")]
    [InlineData("/api/products?pageSize=0")]
    [InlineData("/api/products?pageSize=100000")]
    [InlineData("/api/products?minPrice=50&maxPrice=10")]
    [InlineData("/api/products/1/reviews?page=-1")]
    public async Task مدخلات_ترقيم_غير_صالحة_تُرفض_بـ_400_لا_500(string url)
    {
        // Phase 0 C9: page=0 كان يُنتج OFFSET سالباً ⇒ خطأ SQL ⇒ 500.
        (await _api.Anonymous().GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task الحد_الأعلى_للترقيم_مقبول()
    {
        (await _api.Anonymous().GetAsync("/api/products?page=1&pageSize=100")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ILogger<ConsoleEmailService> من ILogger عام — يكفي لاختبار الخدمة خارج DI.
    private sealed class LoggerAdapter(ILogger inner) : ILogger<ConsoleEmailService>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => inner.BeginScope(state);
        public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => inner.Log(logLevel, eventId, state, exception, formatter);
    }
}
