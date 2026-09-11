using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Souq.Application.Common.Interfaces;
using Testcontainers.MsSql;

namespace Souq.IntegrationTests.Infrastructure;

// ============================================================================
// SouqApiFactory — خادم الـ API الحقيقي (Program.cs كاملاً: الوسطاء، المصادقة، الهجرات،
// البذر) فوق SQL Server حقيقي داخل حاوية مؤقتة. لماذا لا قاعدة في الذاكرة؟ لأن ما
// نختبره هنا سلوك المحرّك نفسه: rowversion، القيود الفريدة، دقّة decimal، تطبيق
// الهجرات — ولاحقاً مرشّحات المستأجرين (ADR-0015).
//
// حاوية واحدة لكل تشغيل (Collection Fixture)؛ كل اختبار ينشئ بياناته بقيم فريدة
// بدل تصفير القاعدة — أسرع وبلا تبعية إضافية.
// ============================================================================
public sealed class SouqApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminEmail = "it-admin@souq.test";
    public const string AdminPassword = "Integration-Admin-2026!";

    private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public CapturingEmailService Emails { get; } = new();
    public CapturingLoggerProvider Logs { get; } = new();
    public string UploadsRoot { get; } = Path.Combine(Path.GetTempPath(), $"souq-it-uploads-{Guid.NewGuid():N}");

    // لاختبارات تبني AppDbContext بإعدادات خاصة (معترِض بساعة ثابتة) فوق نفس القاعدة.
    public string ConnectionString => _sql.GetConnectionString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // "Testing" لا Development: لا user-secrets للمطوّر، ولا مدير افتراضي — المدير
        // يُبذَر من إعداد صريح تماماً كما في الإنتاج (نختبر مسار الإقلاع الحقيقي).
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", _sql.GetConnectionString());
        builder.UseSetting("Jwt:Key", "integration-tests-only-signing-key-0123456789abcdef0123456789");
        builder.UseSetting("Seed:AdminEmail", AdminEmail);
        builder.UseSetting("Seed:AdminPassword", AdminPassword);
        builder.UseSetting("Storage:Local:RootPath", UploadsRoot);

        builder.ConfigureLogging(logging =>
        {
            logging.AddProvider(Logs);
            // أوامر SQL المنفَّذة تُلتقط دائماً لمزوّد الاختبار (أياً كان مستوى الإعداد) كي تعدّ
            // اختبارات N+1 الاستعلامات فعلياً بدل الافتراض.
            logging.AddFilter<CapturingLoggerProvider>("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Information);
        });
        builder.ConfigureTestServices(services =>
        {
            // نلتقط البريد بدل إرساله — لنقرأ رمز إعادة التعيين كما يصل للمستخدم.
            services.RemoveAll<IEmailService>();
            services.AddSingleton<IEmailService>(Emails);
        });
    }

    public async Task InitializeAsync() => await _sql.StartAsync();

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _sql.DisposeAsync();
        if (Directory.Exists(UploadsRoot)) Directory.Delete(UploadsRoot, recursive: true);
    }
}

[CollectionDefinition(Name)]
public sealed class IntegrationCollection : ICollectionFixture<SouqApiFactory>
{
    public const string Name = "integration";
}
