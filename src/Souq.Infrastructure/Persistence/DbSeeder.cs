using Microsoft.EntityFrameworkCore;
using Souq.Domain.Entities;
using Souq.Domain.ValueObjects;

namespace Souq.Infrastructure.Persistence;

// يملأ قاعدة البيانات ببيانات أولية عند أول تشغيل (للتجربة المباشرة).
public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db)
    {
        await db.Database.MigrateAsync();          // يطبّق الهجرات تلقائياً
        if (await db.Categories.AnyAsync()) return; // لا نكرّر البذر

        var electronics = new Category("إلكترونيات", "electronics");
        var fashion = new Category("أزياء", "fashion");
        var home = new Category("منزل", "home");
        db.Categories.AddRange(electronics, fashion, home);
        await db.SaveChangesAsync();

        db.Products.AddRange(
            new Product("سمّاعات لاسلكية", "صوت نقي وعزل ضوضاء فعّال", new Money(59.900m), 25, "headphones", electronics.Id),
            new Product("ساعة ذكية", "تتبّع اللياقة والإشعارات", new Money(120.000m), 12, "watch", electronics.Id),
            new Product("لوحة مفاتيح ميكانيكية", "إضاءة خلفية ومفاتيح مريحة", new Money(45.500m), 30, "keyboard", electronics.Id),
            new Product("حقيبة ظهر جلدية", "تصميم أنيق ومتين للعمل والسفر", new Money(35.000m), 18, "backpack", fashion.Id),
            new Product("نظّارة شمسية", "حماية UV وإطار خفيف", new Money(22.000m), 40, "sunglasses", fashion.Id),
            new Product("مصباح مكتب LED", "إضاءة قابلة للتعديل وموفّرة للطاقة", new Money(18.750m), 50, "lamp", home.Id),
            new Product("ركوة قهوة نحاسية", "صناعة يدوية لقهوة عربية أصيلة", new Money(28.000m), 15, "coffeepot", home.Id),
            new Product("كوب حراري", "يحفظ الحرارة 12 ساعة", new Money(14.500m), 60, "mug", home.Id)
        );

        // مستخدم مدير افتراضي (كلمة المرور هنا hash تجريبي).
        db.Customers.Add(new Customer("مدير المتجر", "admin@souq.com", "HASHED_admin123", "Admin"));
        await db.SaveChangesAsync();
    }
}
