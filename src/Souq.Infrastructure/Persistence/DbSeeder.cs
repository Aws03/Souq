using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Tenancy;
using Souq.Domain.Common;
using Souq.Domain.Entities;
using Souq.Domain.Identity;
using Souq.Domain.Platform;
using Souq.Domain.ValueObjects;
using Souq.Infrastructure.Services;
using Souq.Infrastructure.Tenancy;

namespace Souq.Infrastructure.Persistence;

// إعداد البذر عند الإقلاع — كله من إعداد صريح (متغيّرات بيئة/user-secrets):
//   AdminEmail/AdminPassword                 — أول مدير للمتجر الافتراضي (TenantAdmin).
//   PlatformOwnerEmail/PlatformOwnerPassword — مالك المنصّة (PlatformOwner، بلا متجر) — ADR-0010.
//   DefaultTenantHosts                       — مضيفون يُربطون بالمتجر الافتراضي إن لم يكونوا مربوطين.
// بيانات تطوير افتراضية في Development فقط؛ خارجها لا حساب بلا إعداد صريح، ولا كلمة مرور ضعيفة.
public sealed record SeedOptions(
    string? AdminEmail, string? AdminPassword, bool IsDevelopment, IReadOnlyList<string> DefaultTenantHosts,
    string? PlatformOwnerEmail = null, string? PlatformOwnerPassword = null);

// يطبّق الهجرات ثم يبذر مالك المنصّة (نطاق المنصّة) والمتجر الافتراضي (نطاقه) — كل صف في نطاقه.
public static class DbSeeder
{
    // المتجر الافتراضي أنشأته الهجرة Phase2MultiTenancy (P-04: "Souq" المنصّة، "Marka" أول متجر
    // تجريبي) — كل البيانات السابقة للمرحلة 2 تنتمي إليه.
    public const string DefaultTenantSlug = "marka";

    // بيانات التطوير المحلي فقط — لا تُستخدم خارج Development أبداً. قبل Phase 1A كان مدير بكلمة
    // مرور منشورة يُبذَر في كل البيئات بما فيها Production (Phase 0 B1): باب خلفي في كل نشر.
    public const string DevelopmentAdminEmail = "admin@souq.com";
    public const string DevelopmentAdminPassword = "Admin@123";
    public const string DevelopmentPlatformOwnerEmail = "owner@souq.com";
    public const string DevelopmentPlatformOwnerPassword = "Owner@12345";
    public const int MinimumAdminPasswordLength = 12;

    public static async Task SeedAsync(IServiceProvider services, SeedOptions options, ILogger logger)
    {
        TenantInfo? defaultTenant;
        await using (var scope = services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync();               // يطبّق الهجرات تلقائياً
            await BindDefaultTenantHostsAsync(db, options.DefaultTenantHosts, logger);
            defaultTenant = await scope.ServiceProvider.GetRequiredService<ITenantDirectory>()
                .FindBySlugAsync(DefaultTenantSlug);
        }

        await SeedPlatformOwnerAsync(services, options, logger);

        if (defaultTenant is null)
        {
            logger.LogWarning("Default store '{Slug}' not found; store seeding skipped", DefaultTenantSlug);
            return;
        }

        await TenantScopes.RunAsync(services, defaultTenant, async tenantServices =>
        {
            var db = tenantServices.GetRequiredService<AppDbContext>();
            await SeedCatalogAsync(db, defaultTenant.Currency);
            await SeedAccountAsync(db, tenantServices.GetRequiredService<IPasswordHasher>(), logger,
                options.AdminEmail, options.AdminPassword, options.IsDevelopment,
                DevelopmentAdminEmail, DevelopmentAdminPassword, "مدير المتجر", Roles.TenantAdmin, "Seed:AdminEmail/Seed:AdminPassword");
        });
    }

    // مالك المنصّة في نطاق المنصّة (TenantId = null) — لا يرى متجراً إلا عبر مسار المنصّة المُدقَّق.
    private static async Task SeedPlatformOwnerAsync(IServiceProvider services, SeedOptions options, ILogger logger)
    {
        await using var scope = services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().UsePlatform();
        await SeedAccountAsync(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(), scope.ServiceProvider.GetRequiredService<IPasswordHasher>(),
            logger, options.PlatformOwnerEmail, options.PlatformOwnerPassword, options.IsDevelopment,
            DevelopmentPlatformOwnerEmail, DevelopmentPlatformOwnerPassword, "مالك المنصّة", Roles.PlatformOwner,
            "Seed:PlatformOwnerEmail/Seed:PlatformOwnerPassword");
    }

    private static async Task BindDefaultTenantHostsAsync(AppDbContext db, IReadOnlyList<string> hosts, ILogger logger)
    {
        if (hosts.Count == 0) return;

        var tenant = await db.Tenants.Include(t => t.Domains).FirstOrDefaultAsync(t => t.Slug == DefaultTenantSlug);
        if (tenant is null) return;

        foreach (var raw in hosts)
        {
            var host = TenantDomain.TryNormalizeHost(raw);
            if (host is null)
            {
                logger.LogWarning("Seed:DefaultTenantHosts: invalid host '{Host}' ignored", raw);
                continue;
            }
            // مربوط مسبقاً — لهذا المتجر أو لغيره (لا نسرق نطاق متجر آخر أبداً).
            if (await db.TenantDomains.AnyAsync(d => d.Host == host)) continue;

            tenant.AddDomain(host);
            logger.LogInformation("Bound host {Host} to the default store", host);
        }
        await db.SaveChangesAsync();
    }

    private static async Task SeedCatalogAsync(AppDbContext db, string currency)
    {
        if (await db.Categories.AnyAsync()) return;     // لا نكرّر بذر الكتالوج (مُرشَّح بالمتجر)

        var electronics = new Category("إلكترونيات", "electronics");
        var fashion = new Category("أزياء", "fashion");
        var home = new Category("منزل", "home");
        db.Categories.AddRange(electronics, fashion, home);
        await db.SaveChangesAsync();

        Money Price(decimal amount) => new(amount, currency);
        db.Products.AddRange(
            new Product("سمّاعات لاسلكية", "صوت نقي وعزل ضوضاء فعّال", Price(59.900m), 25, "headphones", electronics.Id, nameEn: "Wireless Headphones"),
            new Product("ساعة ذكية", "تتبّع اللياقة والإشعارات", Price(120.000m), 12, "watch", electronics.Id, nameEn: "Smart Watch"),
            new Product("لوحة مفاتيح ميكانيكية", "إضاءة خلفية ومفاتيح مريحة", Price(45.500m), 30, "keyboard", electronics.Id, nameEn: "Mechanical Keyboard"),
            new Product("حقيبة ظهر جلدية", "تصميم أنيق ومتين للعمل والسفر", Price(35.000m), 18, "backpack", fashion.Id, nameEn: "Leather Backpack"),
            new Product("نظّارة شمسية", "حماية UV وإطار خفيف", Price(22.000m), 40, "sunglasses", fashion.Id, nameEn: "Sunglasses"),
            new Product("مصباح مكتب LED", "إضاءة قابلة للتعديل وموفّرة للطاقة", Price(18.750m), 50, "lamp", home.Id, nameEn: "LED Desk Lamp"),
            new Product("ركوة قهوة نحاسية", "صناعة يدوية لقهوة عربية أصيلة", Price(28.000m), 15, "coffeepot", home.Id, nameEn: "Copper Coffee Pot"),
            new Product("كوب حراري", "يحفظ الحرارة 12 ساعة", Price(14.500m), 60, "mug", home.Id, nameEn: "Thermal Mug")
        );
        await db.SaveChangesAsync();
    }

    // حساب بذرة واحد (مدير متجر أو مالك منصّة) في نطاق مضبوط مسبقاً. لا يغيّر أبداً كلمة مرور حساب
    // صالح موجود؛ يُصلح فقط تجزئة قديمة غير BCrypt لا تصلح للدخول إطلاقاً.
    private static async Task SeedAccountAsync(
        AppDbContext db, IPasswordHasher hasher, ILogger logger,
        string? configuredEmail, string? configuredPassword, bool isDevelopment,
        string developmentEmail, string developmentPassword, string fullName, string role, string settingNames)
    {
        var email = FirstNonEmpty(configuredEmail, isDevelopment ? developmentEmail : null)?.Trim().ToLowerInvariant();
        var password = FirstNonEmpty(configuredPassword, isDevelopment ? developmentPassword : null);

        if (email is null || password is null)
        {
            logger.LogWarning("No {Role} account seeded: set {Settings} (environment variables/user-secrets)", role, settingNames);
            return;
        }

        // خارج التطوير: كلمة مرور مضبوطة صراحةً وقوية بما يكفي — فشل صريح عند الإقلاع أفضل من
        // حساب بصلاحيات واسعة بكلمة مرور ضعيفة يعمل بصمت في الإنتاج.
        if (!isDevelopment && (password.Length < MinimumAdminPasswordLength || password == developmentPassword))
            throw new InvalidOperationException(
                $"كلمة مرور البذرة ({settingNames}) ضعيفة: يلزم {MinimumAdminPasswordLength} حرفاً على الأقل ولا تساوي كلمة مرور التطوير.");

        var normalized = User.NormalizeEmail(email);
        var existing = await db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalized);
        if (existing is null)
        {
            db.Users.Add(new User(fullName, email, hasher.Hash(password), role));
            await db.SaveChangesAsync();
            logger.LogInformation("Seeded {Role} account {Email}", role, LogRedaction.MaskEmail(email));
        }
        else if (!existing.PasswordHash.StartsWith("$2", StringComparison.Ordinal))
        {
            existing.UpgradePasswordHash(hasher.Hash(password));
            await db.SaveChangesAsync();
        }
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
