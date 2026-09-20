using System.Text.RegularExpressions;
using AwesomeAssertions;

namespace Souq.ArchitectureTests;

// ============================================================================
// الواجهة البيضاء في الخادم (المرحلة 15، WhiteLabel.md §6، A4/A5): لا اسم متجر بعينه ولا عملة مكتوبة في شيفرة المنتج ولا في إعداده
// المرفوع — الاسم والعملة والهوية من إعداد كل متجر. الاستثناءان مقصودان: بذر المتجر الافتراضي (بياناته، لا منطق المنتج)، وجدول خانات
// العملات الصغرى (ISO 4217). الهجرات تاريخ مكتوب لا يُعدَّل. نظير اختبار whiteLabel.test.js في الواجهة.
// ============================================================================
public partial class WhiteLabelSourceTests
{
    private static readonly string[] Allowed =
    [
        Path.Combine("Souq.Infrastructure", "Persistence", "DbSeeder.cs"),
        Path.Combine("Souq.Domain", "ValueObjects", "CurrencyInfo.cs"),
    ];

    [Fact]
    public void لا_علامة_متجر_ولا_عملة_مكتوبة_في_شيفرة_المنتج()
    {
        var src = Path.Combine(RepositoryRoot(), "src");
        var files = Directory.EnumerateFiles(src, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || Path.GetFileName(f).StartsWith("appsettings", StringComparison.Ordinal))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();
        files.Should().HaveCountGreaterThan(200, "يجب أن يفحص الاختبار الشيفرة كلها");

        var offenders = files
            .Where(f => !Allowed.Any(a => f.EndsWith(a, StringComparison.Ordinal)))
            .Where(f => Forbidden().IsMatch(File.ReadAllText(f)))
            .Select(f => Path.GetRelativePath(src, f))
            .ToList();

        offenders.Should().BeEmpty("الاسم والعملة من إعداد المتجر — لا من الشيفرة");
    }

    // اسم المتجر التجريبي الأول، ورمز عملته، ورمزها العربي.
    [GeneratedRegex(@"\bMarka\b|ماركة|""JOD""|د\.أ", RegexOptions.IgnoreCase)]
    private static partial Regex Forbidden();

    // الخادم ينشر قائمة الشبكات (`GET /api/admin/store/settings/options`) كي لا تحتفظ الواجهة بنسخة من
    // القاعدة — لكن التذييل يحتاج **أيقونة** لكل شبكة، والأيقونة شيفرة لا بيانات. كان فيه ثلاث من ثمانٍ،
    // فمتجرٌ يضيف تيك‑توك أو واتساب يرى الكلمة بحروف لاتينية صغيرة بين الأيقونات. الفجوة صامتة لأنّ
    // الطرفين صحيحان كلٌّ وحده؛ هذا الاختبار وحده يرى الطرفين معاً.
    [Fact]
    public void كل_شبكة_يقبلها_النطاق_لها_أيقونة_في_التذييل()
    {
        var footer = File.ReadAllText(Path.Combine(RepositoryRoot(), "frontend", "src", "components", "layout", "Footer.jsx"));
        var map = Regex.Match(footer, @"SOCIAL_ICONS\s*=\s*\{(?<body>[^}]*)\}", RegexOptions.Singleline);
        map.Success.Should().BeTrue("التذييل يربط كل شبكة بأيقونتها في SOCIAL_ICONS");

        var mapped = Regex.Matches(map.Groups["body"].Value, @"(?<key>[A-Za-z]+)\s*:")
            .Select(m => m.Groups["key"].Value).ToHashSet(StringComparer.Ordinal);

        var missing = Souq.Domain.Platform.SocialLink.Networks.Keys.Where(n => !mapped.Contains(n)).ToList();
        missing.Should().BeEmpty("شبكة بلا أيقونة تُرسَم اسماً خامّاً في تذييل المتجر");
    }

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "src", "Souq.Domain")))
                return dir.FullName;
        throw new InvalidOperationException("جذر المستودع غير موجود فوق مجلّد الاختبار");
    }
}
