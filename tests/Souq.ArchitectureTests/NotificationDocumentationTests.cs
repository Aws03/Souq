using System.Reflection;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Souq.Application.Common.Notifications;
using Souq.Domain.Entities;

namespace Souq.ArchitectureTests;

// ============================================================================
// قائمة الإشعارات في وثيقة الوحدة تطابق الكود (M14، معيار قبوله الثاني).
//
// **ولمَ اختبارٌ وقد قُرئت القائمتان وطابقتا اليوم؟** لأنّ المطابقة اليوم لا تُبقيها مطابقةً غداً، وهذا
// بالضبط نوع الانحراف الذي لا يراه أحد: من يُضيف نوع رسالةٍ سابعاً يُضيفه إلى قائمة السماح ويشحن، ولا شيء
// في البناء يذكّره بالجدول في الوثيقة. اختبارات التوثيق القائمة تفحص أنّ كل اسمٍ مكتوب **موجود**؛ لا شيء
// فيها يفحص أنّ كل اسمٍ موجود **مكتوب** — والنقص هو الاتجاه الذي يُضلّل قارئ الوثيقة.
//
// ADR-0033 وعد بإشعارَي "مراجعة معلّقة" و"قرار مراجعة" ولم يُبنيا؛ فهرس الـ ADR يسجّل ذلك في §4. هذا
// الاختبار هو ما يجعل تلك الملاحظة تبقى صادقة: يوم يُبنى أحدهما، تفشل هذه المطابقة حتى تُحدَّث الوثيقة.
// ============================================================================
public class NotificationDocumentationTests
{
    private static readonly string Readme =
        File.ReadAllText(Path.Combine(RepositoryRoot(), "docs/04-MODULES/Notifications/README.md"));

    // الصفّ الأول في كل سطر جدول: `| \`Name\` | ... |`
    private static readonly Regex FirstCell = new(@"^\|\s*`(?<name>\w+)`\s*\|", RegexOptions.Multiline | RegexOptions.Compiled);

    [Fact]
    public void أنواع_رسائل_الصادر_في_الكود_هي_نفسها_المكتوبة_في_وثيقة_الوحدة()
    {
        var registered = RegisteredMessageNames();
        registered.Should().NotBeEmpty("قائمة السماح قُرئت بالانعكاس — تغيّر شكلها يُبطل هذا الاختبار بصمت");

        var documented = DocumentedNames("| Message | Handler | What it does |");

        documented.Should().BeEquivalentTo(registered,
            "جدول رسائل الصادر في Notifications/README.md يجب أن يطابق NotificationMessageTypes تماماً — "
            + "نوعٌ جديد بلا صفّ يجعل الوثيقة ناقصة، وصفٌّ بلا نوع يجعلها كاذبة");
    }

    [Fact]
    public void أنواع_الإشعار_داخل_التطبيق_مذكورة_كلّها_في_وثيقة_الوحدة()
    {
        var kinds = typeof(NotificationKinds)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        kinds.Should().NotBeEmpty();
        foreach (var kind in kinds)
            Readme.Should().Contain($"`{kind}`", $"النوع {kind} يصل الواجهة ولا يُذكر في وثيقة الوحدة");
    }

    // قائمة السماح حقلٌ خاصّ عمداً (لا يُحمَّل نوعٌ باسمٍ قادم من القاعدة)، فتُقرأ بالانعكاس بدل فتحها
    // للإنتاج من أجل اختبار. وإن فشلت القراءة فالاختبار يفشل صراحةً لا يمرّ فارغاً.
    private static IReadOnlyCollection<string> RegisteredMessageNames()
    {
        var field = typeof(NotificationMessageTypes)
            .GetField("ByName", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException(
                "تغيّر اسم قائمة السماح في NotificationMessageTypes — حدّث هذا الاختبار بدل تعطيله");

        var map = (System.Collections.IDictionary)field.GetValue(null)!;
        return map.Keys.Cast<string>().ToList();
    }

    private static IReadOnlyCollection<string> DocumentedNames(string header)
    {
        var start = Readme.IndexOf(header, StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, $"لم يُعثر على الجدول برأسه: {header}");

        var body = Readme[start..];
        var end = body.IndexOf("\n## ", StringComparison.Ordinal);
        if (end > -1) body = body[..end];

        return FirstCell.Matches(body).Select(m => m.Groups["name"].Value).ToList();
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Souq.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("لم يُعثر على جذر المستودع");
    }
}
