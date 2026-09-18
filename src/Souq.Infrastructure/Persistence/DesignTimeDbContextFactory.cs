using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Souq.Application.Common.Tenancy;

namespace Souq.Infrastructure.Persistence;

// ============================================================================
// مصنع `AppDbContext` لأدوات التصميم (M17، TD-18).
//
// **المشكلة التي يحلّها**: بلا هذا الصنف، `dotnet ef` يُقلع مضيف الـ API ليحصل على السياق — فيحتاج كل ما
// يحتاجه الإقلاع: سلسلة اتصال صالحة، ومفتاح JWT، ومزوّد دفع، وأسراراً. أي أنّ الأمر المكتوب في
// `CLAUDE.md` و`Migrations.md` **لا يعمل من نسخةٍ نظيفة**، وهو ما سجّله TD-18.
//
// ولمّا صارت خطوة الترحيل المتعمّدة خياراً (R-18)، صار هذا شرطاً لا تحسيناً: الخطوة تُنفَّذ من آلة نشرٍ
// أو من حزمة هجرات، وكلاهما لا يملك — ولا يجوز أن يملك — أسرار التشغيل. الهجرة تحتاج سلسلة اتصال، لا أكثر.
//
// **والسلسلة تُقرأ من البيئة وحدها**، بلا ملفّ إعدادات ولا أسرار مستخدم: `ConnectionStrings__Migrations`
// إن وُجدت (هوية الترحيل ذات صلاحيات المخطّط، R-12)، وإلّا `ConnectionStrings__Default`. وإن غابتا معاً
// تُستعمل سلسلةٌ محلّية مكشوفة لا تصلح إلا لبناء نموذجٍ في الذاكرة — وهو ما يفعله `migrations add`.
//
// ولا يُستعمل هذا المصنع في التشغيل إطلاقاً: EF Core لا يستدعيه إلا من أدوات سطر الأوامر.
// ============================================================================
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    // لا قاعدة حقيقية خلفها: `migrations add` و`migrations script` يبنيان النموذج بلا اتصال، وهذه
    // السلسلة تكفيهما. أمّا `database update` فيمرّر سلسلته من البيئة، فلا تُستعمل هذه.
    private const string ModelOnlyConnection = "Server=(localdb)\\mssqllocaldb;Database=SouqDesignTime";

    public AppDbContext CreateDbContext(string[] args)
    {
        var connection =
            Environment.GetEnvironmentVariable("ConnectionStrings__Migrations")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? ModelOnlyConnection;

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connection, sql => sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .Options;

        // سياق مستأجر فارغ: الهجرات تعمل على المخطّط لا على صفوف متجر، ومرشّح المستأجر لا يُقيَّم هنا.
        return new AppDbContext(options, new TenantContext());
    }
}
