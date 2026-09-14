using System.Text.Json;
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
