using Souq.Domain.Common;
using Souq.Domain.Exceptions;
using Souq.Domain.Platform;
using Souq.Domain.ValueObjects;

namespace Souq.Domain.Entities;

// ============================================================================
// مرادف بحث يُعلِّمه التاجر لمتجره (M3، ADR-0042): كلمة يكتبها المتسوّق ⇒ كلمة تُبحث معها.
//
// **لماذا موجَّه (كلمة ← كلمة) لا مجموعة كلمات متكافئة؟** لأنّ المجموعة تُلزم التعدّي: أضف {أ،ب} و{ب،ج}
// فيصير أ مكافئاً لـ ج بلا أن يقول ذلك أحد، ويصعب على التاجر أن يفهم لماذا ظهرت نتيجة. الاتجاه الصريح يُقرأ
// كما كُتب: "من يكتب هوفر يقصد مكنسة". ومن أراد الاتجاهين أضاف صفّين — وهما صفّان يراهما ويحذف أحدهما.
//
// **التوسيع من مستوى واحد فقط**، لا تعدٍّ: مرادف المرادف لا يُطبَّق. بلا هذا القيد تصير حلقةٌ في البيانات
// (أ←ب، ب←أ) استعلاماً لا ينتهي، ويصير أثر صفٍّ واحد غير مرئي لمن أضافه.
//
// **كلمة واحدة على كل طرف**: التوسيع يجري على مستوى الكلمة داخل استعلام شروطه AND، فتوسيع كلمة إلى عبارة
// يعني شرطاً داخل شرط بدلالة غامضة. عبارة ⇒ صفّان، كلٌّ بكلمته — أوضح للتاجر وأصحّ في المطابقة.
//
// الصورة المطبَّعة مخزَّنة إلى جانب ما كتبه التاجر: المطابقة على الأولى، والعرض في لوحة الإدارة بالثانية —
// فلا يرى التاجر "مكنسه" مكان ما كتبه هو. تُكتبان معاً في Update حصراً، فلا تتقادم إحداهما عن الأخرى.
// ============================================================================
public class SearchSynonym : Entity, ITenantOwned
{
    public const int TermMaxLength = 100;

    // حدّ لكل متجر: المفردات تُقرأ في **كل** بحث، فعددها يدخل زمن الاستجابة مباشرةً. مئتان أكثر من أي متجر
    // حقيقي يحتاجه، وأقلّ بكثير من أن يُلمَس على المسار الساخن. الحدّ قاعدة متجر لا تفصيلة واجهة، فمكانه هنا.
    public const int MaxPerStore = 200;

    public int TenantId { get; private set; }
    public string Culture { get; private set; } = default!;
    public string Term { get; private set; } = default!;
    public string TermNormalized { get; private set; } = default!;
    public string Expansion { get; private set; } = default!;
    public string ExpansionNormalized { get; private set; } = default!;

    private SearchSynonym() { }

    public SearchSynonym(string? culture, string? term, string? expansion) => Update(culture, term, expansion);

    // تعديل كامل للصفّ: اللغة والزوج معاً. لغة الصفّ قابلة للتصحيح كالكلمتين — وإلّا كان على التاجر أن يحذف
    // ويُضيف لتصحيح تسمية.
    public void Update(string? culture, string? term, string? expansion)
    {
        var normalizedCulture = NormalizeCulture(culture);
        var (word, wordNormalized) = Word(term, "الكلمة");
        var (into, intoNormalized) = Word(expansion, "المرادف");

        // كلمة إلى نفسها لا تفعل شيئاً — والمقارنة على الصورة المطبَّعة لأنّ "مكنسة" و"مكنسه" كلمة واحدة هنا.
        if (wordNormalized == intoNormalized)
            throw Invalid("الكلمة ومرادفها متطابقان بعد التطبيع، فالصفّ بلا أثر");

        Culture = normalizedCulture;
        Term = word;
        TermNormalized = wordNormalized;
        Expansion = into;
        ExpansionNormalized = intoNormalized;
    }

    private static string NormalizeCulture(string? culture)
    {
        var normalized = culture?.Trim().ToLowerInvariant() ?? "";
        return Tenant.SupportedCultures.Contains(normalized)
            ? normalized
            : throw Invalid($"لغة غير مدعومة: {culture}");
    }

    private static (string Raw, string Normalized) Word(string? value, string what)
    {
        var trimmed = value?.Trim() ?? "";
        if (trimmed.Length == 0) throw Invalid($"{what} مطلوبة");
        if (trimmed.Length > TermMaxLength) throw Invalid($"{what} تتجاوز {TermMaxLength} حرفاً");

        var tokens = SearchText.Tokenize(trimmed);
        // صفر كلمات = نصّ بلا حرف ولا رقم (ترقيم فقط): لن يطابق شيئاً أبداً.
        if (tokens.Count == 0) throw Invalid($"{what} لا تحمل حرفاً ولا رقماً");
        if (tokens.Count > 1) throw Invalid($"{what} كلمة واحدة — للعبارة أضف صفّاً لكل كلمة");

        return (trimmed, tokens[0]);
    }

    private static Exception Invalid(string message) => new InvalidSearchSynonymException(message);
}
