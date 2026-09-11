using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Notifications;
using Souq.Application.Common.Tenancy;
using Souq.Domain.Common;
using Souq.Domain.Identity;
using Souq.Domain.Platform;
using Souq.Infrastructure.Persistence;
using Souq.Infrastructure.Persistence.Outbox;
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
    public const string PlatformOwnerEmail = "it-owner@souq.test";
    public const string PlatformOwnerPassword = "Integration-Owner-2026!";
    public const string StoreAdminPassword = "Store-Admin-Pass-2026!";
    public const string DefaultHost = "localhost";
    public const string PlatformHost = "admin.localhost";

    private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public CapturingEmailSender Emails { get; } = new();
    public CapturingLoggerProvider Logs { get; } = new();
    public string UploadsRoot { get; } = Path.Combine(Path.GetTempPath(), $"souq-it-uploads-{Guid.NewGuid():N}");

    // المرحلة 11: سرّ الإشعارات التجريبية الموقَّعة (FakeGateway.Sign)، ومفتاح تشفير أسرار حسابات المتاجر.
    public const string FakeWebhookSecret = "integration-tests-fake-webhook-secret";
    public const string SecretsKeyId = "it";
    private static readonly string SecretsKey = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

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
        builder.UseSetting("Seed:PlatformOwnerEmail", PlatformOwnerEmail);
        builder.UseSetting("Seed:PlatformOwnerPassword", PlatformOwnerPassword);
        builder.UseSetting("Storage:Local:RootPath", UploadsRoot);
        // منسّق انتهاء الحجوزات الدوري معطّل: الاختبارات تشغّل أمر الانتهاء مباشرة ولا تسابقها دورة خلفية.
        builder.UseSetting("Inventory:SweepIntervalSeconds", "0");
        // وكذلك منسّق حذف السلال المنتهية: BasketTests ترسل أمر الحذف مباشرة.
        builder.UseSetting("Basket:CleanupIntervalMinutes", "0");
        // وكذلك مُرسِل صندوق الصادر (المرحلة 14): الاختبارات تشغّل دورته صراحةً (DispatchNotificationsAsync) — حتمية بلا انتظار.
        builder.UseSetting("Notifications:DispatchIntervalSeconds", "0");
        builder.UseSetting("Secrets:ActiveKeyId", SecretsKeyId);
        builder.UseSetting($"Secrets:Keys:{SecretsKeyId}", SecretsKey);
        builder.UseSetting("Payments:Fake:WebhookSecret", FakeWebhookSecret);
        // مئات الاختبارات تدخل من العنوان نفسه: حدود الإنتاج تخنقها. اختبار حدّ المعدّل يضيّقها بمصنع مشتقّ.
        foreach (var policy in new[] { "Auth", "Refresh", "CouponPreview", "Basket" })
            builder.UseSetting($"RateLimiting:{policy}:PermitLimit", "100000");

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
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);
        });
    }

    // دورة إرسال صريحة لما حان وقته في صندوق الصادر — وما ولّدته معالجاته (بريد الطلب بعد إشعاراته) في الدورات التالية.
    public async Task<int> DispatchNotificationsAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<IOutboxProcessor>();
        var total = 0;
        for (var pass = 0; pass < 5; pass++)
        {
            var handled = await processor.ProcessDueAsync(CancellationToken.None);
            if (handled == 0) break;
            total += handled;
        }
        return total;
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
        await CreateStoreUserAsync(info, Roles.TenantAdmin, adminEmail);
        return new TestStore(info, host, adminEmail, StoreAdminPassword);
    }

    // حساب داخل متجر بدور محدّد (مدير، موظّف) — داخل نطاق المتجر كي يختمه حارس الكتابة كما في الإنتاج.
    public async Task<string> CreateStoreUserAsync(TenantInfo tenant, string role, string? email = null)
    {
        email ??= $"{role.ToLowerInvariant()}-{Guid.NewGuid():N}@souq.test";
        await using var scope = await TenantScopeAsync(tenant);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        db.Users.Add(new User("حساب اختبار", email, hasher.Hash(StoreAdminPassword), role));
        await db.SaveChangesAsync();
        return email;
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
