using System.Text.Json;
using System.Text.RegularExpressions;
using AwesomeAssertions;

namespace Souq.ArchitectureTests;

// ============================================================================
// قواعد الإعداد المرفوع (ADR-0020، M6):
//   • لا قيمة سرّية في أي appsettings مُتتبَّع. المفاتيح موجودة وفارغة عمداً — حضورها يوثّق
//     ما يجب ضبطه، وفراغها يضمن أن الضبط يأتي من البيئة أو user-secrets.
//   • .env.example لا يحمل قيمة صالحة للاستعمال. الخطر ليس نظرياً: قيمة نائبة طويلة بما يكفي
//     لتجتاز التحقّق تُنتج نشراً كامل الصلاحية بسرّ منشور في مستودع عام.
// هذان فحصان نصّيان لا يحتاجان تشغيل التطبيق، فمكانهما هنا مع بقية قواعد المصدر.
// ============================================================================
public class ConfigurationSourceTests
{
    // اسم مفتاح يدلّ على سرّ. ActiveKeyId معرّف لا سرّ، وPublishableKey علني بطبيعته لكنه
    // خاصّ بالنشر فلا يُرفع أيضاً — يبقى فارغاً كغيره.
    private static readonly string[] SecretNameParts =
        ["key", "password", "secret", "token", "apikey", "connection"];

    private static readonly string[] NotSecrets = ["ActiveKeyId", "KeyId"];

    public static TheoryData<string> CommittedAppSettings()
    {
        var data = new TheoryData<string>();
        foreach (var file in RepositoryPaths.Walk("src")
                     .Where(f => Path.GetFileName(f).StartsWith("appsettings", StringComparison.OrdinalIgnoreCase))
                     .Where(f => f.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
            data.Add(RepositoryPaths.Relative(file));
        return data;
    }

    [Theory]
    [MemberData(nameof(CommittedAppSettings))]
    public void لا_سرّ_بقيمة_في_أي_إعداد_مرفوع(string relativePath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(RepositoryPaths.Combine(relativePath)));

        Populated(document.RootElement, "")
            .Should().BeEmpty($"{relativePath}: مفاتيح الأسرار تبقى فارغة في الإعداد المرفوع");
    }

    [Fact]
    public void ملف_env_example_لا_يحمل_سرّاً_صالحاً()
    {
        var lines = File.ReadAllLines(RepositoryPaths.Combine(".env.example"))
            .Where(l => !l.TrimStart().StartsWith('#') && l.Contains('=', StringComparison.Ordinal))
            .Select(l => (Name: l[..l.IndexOf('=', StringComparison.Ordinal)].Trim(),
                          Value: l[(l.IndexOf('=', StringComparison.Ordinal) + 1)..].Trim()))
            .Where(p => p.Value.Length > 0)
            .ToList();

        // كل قيمة غير فارغة في المثال إمّا غير سرّية (منفذ، رابط، اسم مزوّد) أو نائبة بوضوح.
        lines.Where(p => IsSecretName(p.Name))
            .Where(p => !Souq.Infrastructure.Services.JwtSettingsValidator.LooksLikePlaceholder(p.Value))
            .Select(p => p.Name)
            .Should().BeEmpty("قيمة سرّية صالحة في .env.example تصبح سرّ الإنتاج لمن ينسخ الملف");
    }

    // ── كل إعدادٍ يرفض التطبيق الإقلاع بدونه، تضبطه الحزمة المشحونة (M18) ──────────────────
    //
    // العطل الذي وقع فعلاً: M17 أضافت حارساً يرمي حين يغيب `Database:MigrateOnStartup` خارج التطوير —
    // وهو حارسٌ صائب — ولم تُضبط القيمة في `docker-compose.yml`. فصار `docker compose up` (وهو مسار
    // النشر الوحيد الموثَّق، وما يفعله كل من يستنسخ المستودع) يسقط باستثناء غير مُعالَج عند الإقلاع.
    // لم يُكتشف لأنّ لا أحد أقلع الحزمة بعد ذلك التغيير: الاختبارات كلّها تعمل خارجها.
    //
    // والقائمة **تُشتقّ من المصدر لا تُكتب**: أي حارسٍ لاحق يُكتب بالصيغة نفسها ("المفتاح غير مضبوط")
    // يدخل هذا الفحص تلقائياً. حارسٌ جديد بلا سطرٍ في الحزمة يُفشل هنا، لا عند أول نشر.
    private static readonly Regex RequiredSetting =
        new(@"""(?<key>[A-Z][A-Za-z]+(?::[A-Za-z]+)+) غير مضبوط", RegexOptions.Compiled);

    // `Key__Sub: value` — مُدخَل بيئة في docker-compose، لا تعليقاً ولا اسماً يحتوي المفتاح.
    private static readonly Regex ComposeEnvironmentEntry =
        new(@"^(?<name>[A-Za-z][A-Za-z0-9_]*):\s", RegexOptions.Compiled);

    [Fact]
    public void كل_إعداد_يمنع_الإقلاع_بغيابه_مضبوط_في_حزمة_docker()
    {
        var startup = File.ReadAllText(RepositoryPaths.Combine("src/Souq.API/Program.cs"));
        var required = RequiredSetting.Matches(startup).Select(m => m.Groups["key"].Value).Distinct().ToList();

        required.Should().NotBeEmpty("الصيغة التي يقرأها هذا الفحص تغيّرت — لا قيمة لفحصٍ لا يجد شيئاً");

        // مُدخَلات بيئةٍ فعلية لا مجرّد ورود النصّ: سطرٌ معلَّق يذكر المفتاح، أو مفتاحٌ آخر يحتويه
        // كجزء من اسمه، يجعل الفحص يمرّ على حزمةٍ لا تُقلع. (وهو ما فعلته أوّل صياغة لهذا الفحص.)
        var supplied = File.ReadAllLines(RepositoryPaths.Combine("docker-compose.yml"))
            .Select(l => l.Trim())
            .Where(l => !l.StartsWith('#'))
            .Select(l => ComposeEnvironmentEntry.Match(l))
            .Where(m => m.Success)
            .Select(m => m.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);

        // متغيّرات البيئة تكتب المفتاح بشرطتين سفليتين بدل النقطتين (اصطلاح .NET).
        required.Where(key => !supplied.Contains(key.Replace(":", "__")))
            .Should().BeEmpty("إعداد يرفض التطبيق الإقلاع بدونه وحزمة docker لا تضبطه ⇒ `docker compose up` يسقط");
    }

    private static bool IsSecretName(string name) =>
        !NotSecrets.Any(safe => name.Contains(safe, StringComparison.OrdinalIgnoreCase))
        && SecretNameParts.Any(part => name.Contains(part, StringComparison.OrdinalIgnoreCase));

    // كل مسار مفتاح يبدو سرّياً وقيمته نصّ غير فارغ.
    private static IEnumerable<string> Populated(JsonElement element, string path)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                    foreach (var found in Populated(property.Value, path.Length == 0 ? property.Name : $"{path}:{property.Name}"))
                        yield return found;
                break;
            case JsonValueKind.String when IsSecretName(path) && element.GetString() is { Length: > 0 }:
                yield return path;
                break;
        }
    }
}
