using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Tenancy;
using Souq.Domain.Common;
using Souq.Domain.Entities;
using Souq.Domain.Platform;
using Souq.Domain.ValueObjects;
using Souq.Infrastructure.Services;
using Souq.Infrastructure.Tenancy;

namespace Souq.Infrastructure.Persistence;

// إعداد البذر عند الإقلاع — كله من إعداد صريح (متغيّرات بيئة/user-secrets):
//   AdminEmail/AdminPassword — أول مدير للمتجر الافتراضي (بيانات تطوير افتراضية في Development فقط).
//   DefaultTenantHosts       — مضيفون يُربطون بالمتجر الافتراضي إن لم يكونوا مربوطين (مثل localhost في
//                              حزمة Docker التجريبية). لا متجر احتياطي ضمني في الإنتاج: الربط قرار مكتوب.
public sealed record SeedOptions(
    string? AdminEmail, string? AdminPassword, bool IsDevelopment, IReadOnlyList<string> DefaultTenantHosts);

// يطبّق الهجرات ثم يبذر المتجر الافتراضي — كل بيانات متجر تُكتب داخل نطاق ذلك المتجر.
public static class DbSeeder
{
    // المتجر الافتراضي أنشأته الهجرة Phase2MultiTenancy (P-04: "Souq" المنصّة، "Marka" أول متجر
    // تجريبي) — كل البيانات السابقة للمرحلة 2 تنتمي إليه.
    public const string DefaultTenantSlug = "marka";

    // بيانات مدير التطوير المحلي فقط — لا تُستخدم خارج Development أبداً. قبل Phase 1A
    // كانت تُبذَر في كل البيئات بما فيها Production (Phase 0 B1): حساب مدير بكلمة مرور
    // منشورة في المستودع = باب خلفي في كل نشر.
    public const string DevelopmentAdminEmail = "admin@souq.com";
    public const string DevelopmentAdminPassword = "Admin@123";
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

        if (defaultTenant is null)
        {
            logger.LogWarning("Default store '{Slug}' not found; store seeding skipped", DefaultTenantSlug);
            return;
        }

        await TenantScopes.RunAsync(services, defaultTenant, async tenantServices =>
        {
            var db = tenantServices.GetRequiredService<AppDbContext>();
            await SeedCatalogAsync(db, defaultTenant.Currency);
            await SeedAdminAsync(db, tenantServices.GetRequiredService<IPasswordHasher>(), options, logger);
        });
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

    private static async Task SeedAdminAsync(AppDbContext db, IPasswordHasher hasher, SeedOptions options, ILogger logger)
    {
        var email = FirstNonEmpty(options.AdminEmail, options.IsDevelopment ? DevelopmentAdminEmail : null)?.Trim().ToLowerInvariant();
        var password = FirstNonEmpty(options.AdminPassword, options.IsDevelopment ? DevelopmentAdminPassword : null);

        if (email is null || password is null)
        {
            logger.LogWarning(
                "لم يُنشأ حساب مدير: اضبط Seed:AdminEmail وSeed:AdminPassword (متغيّرات بيئة/user-secrets) لإنشاء أول مدير.");
            return;
        }

        // خارج التطوير: كلمة مرور مضبوطة صراحةً وقوية بما يكفي — فشل صريح عند الإقلاع
        // أفضل من مدير بكلمة مرور ضعيفة يعمل بصمت في الإنتاج.
        if (!options.IsDevelopment && (password.Length < MinimumAdminPasswordLength || password == DevelopmentAdminPassword))
            throw new InvalidOperationException(
                $"Seed:AdminPassword ضعيفة: يلزم {MinimumAdminPasswordLength} حرفاً على الأقل ولا تساوي كلمة مرور التطوير.");

        var existing = await db.Customers.FirstOrDefaultAsync(c => c.Email == email);
        if (existing is null)
        {
            db.Customers.Add(new Customer("مدير المتجر", email, hasher.Hash(password), Roles.Admin));
            await db.SaveChangesAsync();
            logger.LogInformation("أُنشئ حساب المدير {Email}", LogRedaction.MaskEmail(email));
        }
        else if (!existing.PasswordHash.StartsWith("$2"))
        {
            // تجزئة قديمة غير BCrypt (بذرة "HASHED_admin123" الأولى) لا تصلح للدخول إطلاقاً —
            // نصلحها بكلمة المرور المضبوطة. لا نغيّر أبداً كلمة مرور مدير صالحة موجودة.
            existing.ChangePasswordHash(hasher.Hash(password));
            await db.SaveChangesAsync();
        }
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
