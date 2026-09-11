using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Tenancy;
using Souq.Domain.Common;
using Souq.Domain.Entities;
using Souq.Domain.Platform;
using Souq.Infrastructure.Persistence;
using Testcontainers.MsSql;

namespace Souq.IntegrationTests.Infrastructure;

// ============================================================================
// SouqApiFactory — خادم الـ API الحقيقي (Program.cs كاملاً: الوسطاء، المصادقة، الهجرات،
// البذر) فوق SQL Server حقيقي داخل حاوية مؤقتة. لماذا لا قاعدة في الذاكرة؟ لأن ما
// نختبره هنا سلوك المحرّك نفسه: rowversion، القيود الفريدة، دقّة decimal، تطبيق
// الهجرات، ومرشّحات المستأجرين (ADR-0015).
//
// حاوية واحدة لكل تشغيل (Collection Fixture)؛ كل اختبار ينشئ بياناته بقيم فريدة
// بدل تصفير القاعدة — أسرع وبلا تبعية إضافية. المتجر الافتراضي (marka) يُخدَم على localhost؛
// متاجر إضافية تُنشأ بـ CreateStoreAsync وتُخاطَب بمضيفها الحقيقي (TestApi.ForStore).
// ============================================================================
public sealed class SouqApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminEmail = "it-admin@souq.test";
    public const string AdminPassword = "Integration-Admin-2026!";
    public const string StoreAdminPassword = "Store-Admin-Pass-2026!";
    public const string DefaultHost = "localhost";

    private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public CapturingEmailService Emails { get; } = new();
    public CapturingLoggerProvider Logs { get; } = new();
    public string UploadsRoot { get; } = Path.Combine(Path.GetTempPath(), $"souq-it-uploads-{Guid.NewGuid():N}");

    // لاختبارات تبني AppDbContext بإعدادات خاصة (معترِض بساعة ثابتة، قاعدة تجريب هجرات) فوق نفس الخادم.
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

    public async Task<TenantInfo> TenantAsync(string slug)
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ITenantDirectory>().FindBySlugAsync(slug)
            ?? throw new InvalidOperationException($"No store '{slug}'");
    }

    public Task<TenantInfo> DefaultTenantAsync() => TenantAsync(DbSeeder.DefaultTenantSlug);

    public static TenantContext ContextFor(TenantInfo tenant)
    {
        var context = new TenantContext();
        context.UseTenant(tenant);
        return context;
    }

    // نطاق خدمات داخل متجر — كما يضبطه وسيط التحديد لطلب HTTP (افتراضياً: المتجر الافتراضي).
    public async Task<AsyncServiceScope> TenantScopeAsync(TenantInfo? tenant = null)
    {
        tenant ??= await DefaultTenantAsync();
        var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().UseTenant(tenant);
        return scope;
    }

    // متجر حقيقي إضافي بنطاقه ومديره — كما ستُنشئه المنصّة (المرحلة 4)، لكن مباشرة عبر القاعدة.
    public async Task<TestStore> CreateStoreAsync(TenantStatus status = TenantStatus.Active, string currency = "JOD")
    {
        var slug = $"s{Guid.NewGuid():N}"[..20];
        var host = $"{slug}.store.test";

        await using (var scope = Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = new Tenant($"متجر {slug}", slug, currency, "ar", "Asia/Amman");
            tenant.AddDomain(host);
            if (status is TenantStatus.Active or TenantStatus.Suspended) tenant.Activate();
            if (status == TenantStatus.Suspended) tenant.Suspend();
            if (status == TenantStatus.Archived) tenant.Archive();
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
            scope.ServiceProvider.GetRequiredService<ITenantDirectory>().Invalidate();
        }

        var info = await TenantAsync(slug);
        var adminEmail = $"admin@{host}";
        await using (var scope = await TenantScopeAsync(info))
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            db.Customers.Add(new Customer("مدير المتجر", adminEmail, hasher.Hash(StoreAdminPassword), Roles.Admin));
            await db.SaveChangesAsync();
        }

        return new TestStore(info, host, adminEmail, StoreAdminPassword);
    }

    public async Task InitializeAsync() => await _sql.StartAsync();

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _sql.DisposeAsync();
        if (Directory.Exists(UploadsRoot)) Directory.Delete(UploadsRoot, recursive: true);
    }
}

// متجر اختبار: لقطته، مضيفه، ومديره.
public sealed record TestStore(TenantInfo Tenant, string Host, string AdminEmail, string AdminPassword);

[CollectionDefinition(Name)]
public sealed class IntegrationCollection : ICollectionFixture<SouqApiFactory>
{
    public const string Name = "integration";
}
