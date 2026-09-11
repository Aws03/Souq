using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Souq.Application.Common.Interfaces;
using Souq.Domain.Common;
using Souq.Domain.Entities;
using Souq.Domain.ValueObjects;
using Souq.Infrastructure.Services;

namespace Souq.Infrastructure.Persistence;

// إعداد بذر أول مدير: من الإعداد صراحةً (Seed:AdminEmail/Seed:AdminPassword — متغيّرات
// بيئة/user-secrets)، مع بيانات تطوير افتراضية في Development فقط.
public sealed record AdminSeedOptions(string? Email, string? Password, bool IsDevelopment);

// يطبّق الهجرات ويملأ قاعدة البيانات ببيانات أولية عند الإقلاع.
public static class DbSeeder
{
    // بيانات مدير التطوير المحلي فقط — لا تُستخدم خارج Development أبداً. قبل Phase 1A
    // كانت تُبذَر في كل البيئات بما فيها Production (Phase 0 B1): حساب مدير بكلمة مرور
    // منشورة في المستودع = باب خلفي في كل نشر.
    public const string DevelopmentAdminEmail = "admin@souq.com";
    public const string DevelopmentAdminPassword = "Admin@123";
    public const int MinimumAdminPasswordLength = 12;

    public static async Task SeedAsync(AppDbContext db, IPasswordHasher hasher, AdminSeedOptions admin, ILogger logger)
    {
        await db.Database.MigrateAsync();               // يطبّق الهجرات تلقائياً
        await SeedCatalogAsync(db);
        await SeedAdminAsync(db, hasher, admin, logger);
    }

    private static async Task SeedCatalogAsync(AppDbContext db)
    {
        if (await db.Categories.AnyAsync()) return;     // لا نكرّر بذر الكتالوج

        var electronics = new Category("إلكترونيات", "electronics");
        var fashion = new Category("أزياء", "fashion");
        var home = new Category("منزل", "home");
        db.Categories.AddRange(electronics, fashion, home);
        await db.SaveChangesAsync();

        db.Products.AddRange(
            new Product("سمّاعات لاسلكية", "صوت نقي وعزل ضوضاء فعّال", new Money(59.900m), 25, "headphones", electronics.Id, nameEn: "Wireless Headphones"),
            new Product("ساعة ذكية", "تتبّع اللياقة والإشعارات", new Money(120.000m), 12, "watch", electronics.Id, nameEn: "Smart Watch"),
            new Product("لوحة مفاتيح ميكانيكية", "إضاءة خلفية ومفاتيح مريحة", new Money(45.500m), 30, "keyboard", electronics.Id, nameEn: "Mechanical Keyboard"),
            new Product("حقيبة ظهر جلدية", "تصميم أنيق ومتين للعمل والسفر", new Money(35.000m), 18, "backpack", fashion.Id, nameEn: "Leather Backpack"),
            new Product("نظّارة شمسية", "حماية UV وإطار خفيف", new Money(22.000m), 40, "sunglasses", fashion.Id, nameEn: "Sunglasses"),
            new Product("مصباح مكتب LED", "إضاءة قابلة للتعديل وموفّرة للطاقة", new Money(18.750m), 50, "lamp", home.Id, nameEn: "LED Desk Lamp"),
            new Product("ركوة قهوة نحاسية", "صناعة يدوية لقهوة عربية أصيلة", new Money(28.000m), 15, "coffeepot", home.Id, nameEn: "Copper Coffee Pot"),
            new Product("كوب حراري", "يحفظ الحرارة 12 ساعة", new Money(14.500m), 60, "mug", home.Id, nameEn: "Thermal Mug")
        );
        await db.SaveChangesAsync();
    }

    private static async Task SeedAdminAsync(AppDbContext db, IPasswordHasher hasher, AdminSeedOptions admin, ILogger logger)
    {
        var email = FirstNonEmpty(admin.Email, admin.IsDevelopment ? DevelopmentAdminEmail : null)?.Trim().ToLowerInvariant();
        var password = FirstNonEmpty(admin.Password, admin.IsDevelopment ? DevelopmentAdminPassword : null);

        if (email is null || password is null)
        {
            logger.LogWarning(
                "لم يُنشأ حساب مدير: اضبط Seed:AdminEmail وSeed:AdminPassword (متغيّرات بيئة/user-secrets) لإنشاء أول مدير.");
            return;
        }

        // خارج التطوير: كلمة مرور مضبوطة صراحةً وقوية بما يكفي — فشل صريح عند الإقلاع
        // أفضل من مدير بكلمة مرور ضعيفة يعمل بصمت في الإنتاج.
        if (!admin.IsDevelopment && (password.Length < MinimumAdminPasswordLength || password == DevelopmentAdminPassword))
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
