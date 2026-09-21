using Microsoft.EntityFrameworkCore;
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

        // ============================================================================
        // فحص النطاقات مُفعَّل هنا صراحةً (M11) — وهو ما كان غائباً.
        //
        // الحقيقة التي كشفها M11: طلبُ خدمةٍ بنطاق من المزوّد الجذري **يمنع الـ API من الإقلاع في
        // Development** (الفحص مُفعَّل هناك تلقائياً) ويمرّ بصمت في Production وTesting (مُطفأ) — فتصير
        // الخدمة بعمر التطبيق. عاش هذا في `SearchIndexBackfill` من M3 إلى M11 لأنّ كلّ تحقّق جرى على
        // حزمة الحاويات وكلّ اختبار تكامل جرى في Testing: بيئتان لا تفحصان، وثالثةٌ تفحص لا يزورها أحد.
        //
        // بتفعيله هنا يصير الخطأ نفسه فشلَ اختبارٍ لا مفاجأةَ مطوّرٍ يستنسخ المستودع. و`ValidateOnBuild`
        // يمسك ما هو أوسع: تبعيةٌ غير مسجَّلة إطلاقاً تُكتشف عند بناء المزوّد لا عند أول طلب يصادفها.
        // ============================================================================
        builder.UseDefaultServiceProvider(options =>
        {
            options.ValidateScopes = true;
            options.ValidateOnBuild = true;
        });
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
        // وكذلك منسّق مسح سجلّ البحث (M13): SearchAnalyticsTests ترسل أمر المسح مباشرة.
        builder.UseSetting("Search:Log:PurgeIntervalMinutes", "0");
        // أمّا كاتب السجلّ فيبقى يعمل — هو مسار الكتابة نفسه، ولا يُفحص بتعطيله. تُقصَّر نافذة تجميعه وحدها
        // من ثانيتين إلى عشرين مللي ثانية: نفس الكود ونفس النطاقات، بلا انتظارٍ في كل اختبار.
        builder.UseSetting("Search:Log:WriteBatchMilliseconds", "20");
        builder.UseSetting("Secrets:ActiveKeyId", SecretsKeyId);
        builder.UseSetting($"Secrets:Keys:{SecretsKeyId}", SecretsKey);
        builder.UseSetting("Payments:Fake:WebhookSecret", FakeWebhookSecret);
        // مئات الاختبارات تدخل من العنوان نفسه: حدود الإنتاج تخنقها. اختبار حدّ المعدّل يضيّقها بمصنع مشتقّ.
        foreach (var policy in new[] { "Auth", "Refresh", "CouponPreview", "Basket", "Export" })
            builder.UseSetting($"RateLimiting:{policy}:PermitLimit", "100000");

        builder.ConfigureLogging(logging =>
        {
            // نسخة لهذا المضيف بنطاقاته، ومصبّ القيود مشترك — انظر CapturingLoggerProvider.Fork:
            // تسجيل المثيل نفسه في مضيف مشتقّ كان يكتب فوق مزوّد نطاقات المصنع الأساس.
            logging.AddProvider(Logs.Fork());
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

    // ============================================================================
    // متجر على خطة بحدود حقيقية (C2). الخطة التأسيسية بلا حدود عمداً، فاختبارُ الحصص يحتاج عقداً
    // يحمل رقماً — إصدارٌ منشور خاصٌّ بهذا المتجر وحده كي لا تتشارك الاختبارات سقفاً واحداً.
    // ============================================================================
    public async Task<TestStore> CreateStoreOnPlanWithLimitsAsync(params (string Name, int Value)[] limits)
    {
        var store = await CreateStoreAsync();

        await using (var scope = Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var plan = new Plan($"limited-{Guid.NewGuid():N}"[..24], 1, "خطة محدودة");
            plan.SetEntitlements(StoreModules.All);
            plan.SetLimits(limits.Select(l => new Limit(l.Name, l.Value)));
            plan.Publish();
            db.Plans.Add(plan);
            await db.SaveChangesAsync();

            // صفّ اشتراك واحد لكل متجر: نُحوّل القائم بدل إضافة ثانٍ يخرقه الفهرس الفريد.
            var subscription = await db.Subscriptions.FirstAsync(s => s.TenantId == store.Tenant.Id);
            subscription.ChangePlan(plan, DateTime.UtcNow);
            await db.SaveChangesAsync();

            scope.ServiceProvider.GetRequiredService<ITenantDirectory>().Invalidate();
        }

        // اللقطة تُعاد قراءتها: حدود الخطة تعيش فيها، والقديمة بلا حدود.
        return store with { Tenant = await TenantAsync(store.Tenant.Slug) };
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

            // الخطة التأسيسية (C1، ADR-0047): منذ إغلاق الافتراض المفتوح صار المتجر بلا اشتراك
            // متجراً بلا وحدة اختيارية واحدة. المنصّة تُسنِدها في CreateTenantHandler، وهذا المصنع
            // يكتب في القاعدة مباشرةً فيُسنِدها بنفسه — وإلا لأجاب كل اختبار يمسّ الكوبونات أو
            // التقييمات أو المفضّلة بـ 404 ModuleDisabled، وهو فشلٌ يبدو عيباً في تلك الميزات.
            var foundation = await db.Plans
                .Where(p => p.Code == Plan.FoundationCode && p.Status == PlanStatus.Published)
                .OrderByDescending(p => p.Version)
                .FirstOrDefaultAsync();
            if (foundation is not null)
            {
                db.Subscriptions.Add(new Subscription(tenant.Id, foundation, DateTime.UtcNow));
                await db.SaveChangesAsync();
            }

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
