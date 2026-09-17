using AwesomeAssertions;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Souq.ArchitectureTests;

// ============================================================================
// مسار البحث محلّي وحتمي (M3، ADR-0042).
//
// معيار قبول M3 الصريح: "لا استدعاء لواجهة ذكاء اصطناعي على مسار البحث وقت التشغيل". هذا ليس وعداً في وثيقة:
// الوثيقة تتقادم بصمت، وسطرٌ واحد يضيفه أحدهم لاحقاً ("مُعيد ترتيب أذكى") يحوّل البحث إلى اعتماد على خدمة
// خارجية — بزمن استجابة وكلفة وعطلٍ حين تسقط، وبنتائج لا تُفسَّر ولا تُختبَر.
//
// السقّاطة تفحص **استدعاءات الشيفرة الفعلية** لا التصريحات: أي نداء شبكة من خدمات القراءة يُفشل البناء.
// وإضافته قرار معماري يحتاج ADR يَنسخ ADR-0042، لا سطراً يمرّ في مراجعة.
// ============================================================================
public class SearchRuntimeRuleTests
{
    // أنواع تُخرِج طلباً من العملية: أي منها على مسار قراءة يعني اعتماداً خارجياً وقت الاستجابة.
    private static readonly HashSet<string> NetworkTypes = new(StringComparer.Ordinal)
    {
        "System.Net.Http.HttpClient", "System.Net.Http.HttpMessageInvoker", "System.Net.Http.IHttpClientFactory",
        "System.Net.WebClient", "System.Net.Sockets.Socket", "System.Net.WebRequest", "System.Net.HttpWebRequest",
    };

    [Fact]
    public void خدمات_قراءة_الكتالوج_لا_تُجري_أي_نداء_شبكة()
    {
        using var infrastructure = ModuleDefinition.ReadModule(
            typeof(Souq.Infrastructure.DependencyInjection).Assembly.Location);

        var offenders = infrastructure.GetTypes()
            .Where(type => type.FullName.StartsWith("Souq.Infrastructure.Persistence.Queries", StringComparison.Ordinal))
            .SelectMany(type => type.Methods.Where(m => m.HasBody).Select(m => (Type: type, Method: m)))
            .Where(entry => entry.Method.Body.Instructions.Any(UsesNetwork))
            .Select(entry => $"{entry.Type.FullName}.{entry.Method.Name}")
            .Distinct()
            .ToList();

        offenders.Should().BeEmpty(
            "مسار القراءة — والبحث منه — يُجيب من قاعدة البيانات وحدها. نداء شبكة هنا يعني زمناً وكلفةً وعطلاً "
            + "خارج سيطرة المتجر، ونتائج لا تُفسَّر. إضافته قرار معماري بـ ADR يَنسخ ADR-0042، لا سطر كود");
    }

    private static bool UsesNetwork(Instruction instruction)
    {
        var name = instruction.Operand switch
        {
            MethodReference method => method.DeclaringType?.FullName,
            TypeReference type => type.FullName,
            FieldReference field => field.FieldType?.FullName,
            _ => null,
        };
        return name is not null && NetworkTypes.Contains(name);
    }
}
