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
//   SeedDemoData                             — بيانات العرض (كتالوج المتجر الافتراضي ومظهره). انظر DbSeeder.ShouldSeedDemoData.
public sealed record SeedOptions(
    string? AdminEmail, string? AdminPassword, bool IsDevelopment, IReadOnlyList<string> DefaultTenantHosts,
    string? PlatformOwnerEmail = null, string? PlatformOwnerPassword = null, bool SeedDemoData = false);

// يطبّق الهجرات ثم يبذر مالك المنصّة (نطاق المنصّة) والمتجر الافتراضي (نطاقه) — كل صف في نطاقه.
public static class DbSeeder
{
    // المتجر الافتراضي أنشأته الهجرة Phase2MultiTenancy (P-04: "Souq" المنصّة، "Marka" أول متجر
    // تجريبي) — كل البيانات السابقة للمرحلة 2 تنتمي إليه.
    public const string DefaultTenantSlug = "marka";

    // الاسم الذي تكتبه هجرة المرحلة 2 للمتجر الافتراضي. يُقارَن به لاكتشاف متجر لم يتبنّه أحد
    // بعد (أدناه) — ولهذا السبب وحده يعيش هذا النصّ هنا (WhiteLabelSourceTests يسمح لهذا الملف).
    public const string DefaultTenantSeededName = "Marka Demo";

    // بيانات التطوير المحلي فقط — لا تُستخدم خارج Development أبداً. قبل Phase 1A كان مدير بكلمة
    // مرور منشورة يُبذَر في كل البيئات بما فيها Production (Phase 0 B1): باب خلفي في كل نشر.
    public const string DevelopmentAdminEmail = "admin@souq.com";
    public const string DevelopmentAdminPassword = "Admin@123";
    public const string DevelopmentPlatformOwnerEmail = "owner@souq.com";
    public const string DevelopmentPlatformOwnerPassword = "Owner@12345";
    public const int MinimumAdminPasswordLength = 12;

    // بيانات العرض تُبذَر في التطوير والاختبار وحدهما، أو بطلب صريح (Seed:DemoData). قاعدة إنتاج جديدة كانت تُبذَر بمتجر
    // تجريبي بكتالوجه ومظهره وبيانات تواصله بلا أن يطلب أحد ذلك — بيانات غريبة في متجر زبون حقيقي (R-17). الإعداد الصريح
    // يغلب البيئة في الاتجاهين: true لعرض توضيحي على خادم، وfalse لقاعدة تطوير نظيفة.
    public static bool ShouldSeedDemoData(string environmentName, bool? configured) =>
        configured ?? PaymentProviderSelector.IsLocal(environmentName);

    public static async Task SeedAsync(IServiceProvider services, SeedOptions options, ILogger logger)
    {
        // ── الهجرات أولاً، بهويّتها هي (R-12) ──────────────────────────────────
        // سياق منفصل بسلسلة الهجرات: هي وحدها تحتاج صلاحيات تغيير المخطّط، وبعدها يعمل كل شيء
        // بهوية التشغيل. بلا ConnectionStrings:Migrations السلسلتان واحدة ولا يتغيّر شيء.
        var migration = services.GetRequiredService<MigrationConnection>();
        await using (var migrationDb = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(migration.Value).Options, new TenantContext()))
        {
            await migrationDb.Database.MigrateAsync();
        }
        logger.LogInformation("Migrations applied using {MigrationIdentity} identity",
            migration.IsSeparateIdentity ? "a dedicated migration" : "the runtime");

        TenantInfo? defaultTenant;
        await using (var scope = services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await BindDefaultTenantHostsAsync(db, options.DefaultTenantHosts, logger);
            if (options.SeedDemoData) await ApplyDefaultStoreLookAsync(db, logger);
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
            if (options.SeedDemoData) await SeedCatalogAsync(db, defaultTenant.Currency);
            else logger.LogInformation("Demo data not seeded (Seed:DemoData is off for this environment)");
            await SeedAccountAsync(db, tenantServices.GetRequiredService<IPasswordHasher>(), logger,
                options.AdminEmail, options.AdminPassword, options.IsDevelopment,
                DevelopmentAdminEmail, DevelopmentAdminPassword, "مدير المتجر", Roles.TenantAdmin, "Seed:AdminEmail/Seed:AdminPassword");

            if (!options.SeedDemoData) await WarnAboutUnadoptedDefaultStoreAsync(db, defaultTenant, logger);
        });
    }

    // ============================================================================
    // المتجر الافتراضي في قاعدة لم تُبذَر ببيانات عرض (أي: إنتاج/تجهيز).
    //
    // هجرة المرحلة 2 تكتب المتجر رقم 1 في *كل* قاعدة — فهو الوعاء الذي انتقلت إليه بيانات ما
    // قبل تعدّد المتاجر، ولا يمكن حذفه من الهجرة بأثر رجعي (نشرٌ قائم قد يكون تبنّاه وملأه).
    // لكن قاعدة إنتاج جديدة تبدأ إذن بمتجر *فعّال* اسمه اسم العرض التوضيحي، وقد يُربط بنطاق
    // حقيقي عبر Seed:DefaultTenantHosts فيخدم زبائن باسم لم يختره أحد.
    //
    // لا نغيّره ولا نعطّله تلقائياً: كلاهما قرار مالك (قد يكون هذا متجره فعلاً). نقول ما نراه،
    // بوضوح، عند كل إقلاع حتى يُتَّخذ القرار — وخطوات تبنّيه أو أرشفته في SeedAndBootstrap.md.
    // ============================================================================
    private static async Task WarnAboutUnadoptedDefaultStoreAsync(AppDbContext db, TenantInfo tenant, ILogger logger)
    {
        if (tenant.Name == DefaultTenantSeededName)
            logger.LogWarning("Configuration warning: {ConfigurationWarning}",
                $"المتجر الافتراضي ما زال باسم البذر '{DefaultTenantSeededName}' وحالته {tenant.Status}. " +
                "تبنّه (غيّر اسمه وعملته ونطاقه) أو أرشفه قبل استقبال زبائن — docs/09-OPERATIONS/SeedAndBootstrap.md");

        if (tenant.Status == TenantStatus.Active && !await db.Users.AnyAsync(u => u.Role == Roles.TenantAdmin))
            logger.LogWarning("Configuration warning: {ConfigurationWarning}",
                "المتجر الافتراضي فعّال وبلا أي مدير: لا أحد يستطيع إدارته. " +
                "اضبط Seed:AdminEmail و Seed:AdminPassword عند أول إقلاع، أو أنشئ مديره من منطقة المنصّة.");
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

    // المتجر الافتراضي يحتفظ بمظهر ماركة الحالي تماماً (WhiteLabel.md §6): تُضبط إعداداته مرّة واحدة إن لم يضبطها
    // أحد — بعدها هي بيانات يعدّلها مديره كأي متجر، والبذر لا يلمسها ثانيةً. القيم تمرّ بقواعد Domain نفسها.
    private static async Task ApplyDefaultStoreLookAsync(AppDbContext db, ILogger logger)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Slug == DefaultTenantSlug);
        if (tenant is null || tenant.HasCustomSettings) return;

        static Dictionary<string, string?> Text(string ar, string en) => new() { ["ar"] = ar, ["en"] = en };

        tenant.SetEnabledCultures(["ar", "en"]);
        tenant.UpdateBranding(BrandColors.Create("#0F3B3A", "#F0EBE1", "#E8A33D", "#FAF7F1", "#1A2421"), "kufi-tajawal", "classic");
        tenant.UpdateStorefront(
            Text("ماركة", "Marka"),
            StoreContact.Create("support@marka.example", "+962 6 000 0000", Text("عمّان، الأردن", "Amman, Jordan")),
            [],
            SeoSettings.Create(
                Text("ماركة — متجر إلكتروني بهوية عربية", "Marka — an online store with an Arabian identity"),
                Text("ماركة — متجر إلكتروني عصري بهوية عربية أصيلة، منتجات مختارة بعناية وتوصيل سريع.",
                     "Marka — a modern online store with an authentic Arabian identity, carefully curated products and fast delivery.")),
            Text("شحن مجاني للطلبات فوق 50 دينار — توصيل لجميع أنحاء الأردن — ماركة",
                 "Free shipping on orders over 50 JOD — delivery across Jordan — Marka"));
        await db.SaveChangesAsync();
        logger.LogInformation("Applied the Marka look to the default store settings");
    }

    private static async Task SeedCatalogAsync(AppDbContext db, string currency)
    {
        if (await db.Categories.AnyAsync()) return;     // لا نكرّر بذر الكتالوج (مُرشَّح بالمتجر)

        static Dictionary<string, CatalogText> Texts(string ar, string en, string? descriptionAr = null) =>
            new() { ["ar"] = new CatalogText(ar, descriptionAr), ["en"] = new CatalogText(en) };

        var electronics = new Category("electronics", Texts("إلكترونيات", "Electronics"), sortOrder: 0);
        var fashion = new Category("fashion", Texts("أزياء", "Fashion"), sortOrder: 1);
        var home = new Category("home", Texts("منزل", "Home"), sortOrder: 2);
        db.Categories.AddRange(electronics, fashion, home);
        await db.SaveChangesAsync();

        var seeded = new List<(Product Product, int Stock)>();
        void Seed(string slug, string nameAr, string nameEn, string description, decimal price, int stock, int categoryId) =>
            seeded.Add((new Product(slug, categoryId, Texts(nameAr, nameEn, description), new Money(price, currency)), stock));

        Seed("wireless-headphones", "سمّاعات لاسلكية", "Wireless Headphones", "صوت نقي وعزل ضوضاء فعّال", 59.900m, 25, electronics.Id);
        Seed("smart-watch", "ساعة ذكية", "Smart Watch", "تتبّع اللياقة والإشعارات", 120.000m, 12, electronics.Id);
        Seed("mechanical-keyboard", "لوحة مفاتيح ميكانيكية", "Mechanical Keyboard", "إضاءة خلفية ومفاتيح مريحة", 45.500m, 30, electronics.Id);
        Seed("leather-backpack", "حقيبة ظهر جلدية", "Leather Backpack", "تصميم أنيق ومتين للعمل والسفر", 35.000m, 18, fashion.Id);
        Seed("sunglasses", "نظّارة شمسية", "Sunglasses", "حماية UV وإطار خفيف", 22.000m, 40, fashion.Id);
        Seed("led-desk-lamp", "مصباح مكتب LED", "LED Desk Lamp", "إضاءة قابلة للتعديل وموفّرة للطاقة", 18.750m, 50, home.Id);
        Seed("copper-coffee-pot", "ركوة قهوة نحاسية", "Copper Coffee Pot", "صناعة يدوية لقهوة عربية أصيلة", 28.000m, 15, home.Id);
        Seed("thermal-mug", "كوب حراري", "Thermal Mug", "يحفظ الحرارة 12 ساعة", 14.500m, 60, home.Id);
        db.Products.AddRange(seeded.Select(s => s.Product));
        await db.SaveChangesAsync();

        // مخزون كل منتج في وحدة Inventory (المرحلة 6): المخزون أولاً (معرّفه)، ثم كمّيته حركة توريد في السجلّ.
        var stocked = seeded.Select(s => (Item: new InventoryItem(s.Product.Id, s.Product.DefaultVariant.Id), s.Stock)).ToList();
        db.InventoryItems.AddRange(stocked.Select(s => s.Item));
        await db.SaveChangesAsync();
        db.StockMovements.AddRange(stocked.Select(s =>
            s.Item.Receive(s.Stock, Souq.Domain.Enums.StockMovementType.Purchase, "المخزون الابتدائي")));
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
