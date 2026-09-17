using System.Globalization;
using System.Text;

namespace Souq.Domain.ValueObjects;

// ============================================================================
// تطبيع نص البحث (M3، ADR-0042): دالّة خالصة واحدة تُطبَّق على **النص المفهرس ونصّ الاستعلام معاً**. لو طُبِّقت على
// أحدهما فقط لم يتطابق شيء — فلا تُستدعى إلا من CatalogTranslation.Apply (الفهرسة) ومن مسار البحث (الاستعلام).
//
// لماذا في المجال لا في قاعدة البيانات: ترتيب المقارنة (collation) الافتراضي
// SQL_Latin1_General_CP1_CI_AS يهمل حالة الأحرف اللاتينية فقط — أمّا صور الألف (أ إ آ) والتاء المربوطة والألف
// المقصورة والتشكيل فحروف مستقلّة في Unicode لا لهجات، فلا يطويها أي ترتيب مقارنة. ولأنّ التطبيع هنا، لا يعتمد
// البحث على ترتيب مقارنة الخادم إطلاقاً: النتيجة نفسها على أي تنصيب (R-16 كان يرصد هذا الاعتماد).
//
// القاعدة: التطبيع لا يُطيل نصّ ar/en أبداً (يحذف أو يستبدل حرفاً بحرف)، فأعمدة الصورة المطبَّعة بطول أعمدة أصلها.
// ============================================================================
public static class SearchText
{
    // أقصى عدد كلمات يُؤخذ من استعلام واحد: متسوّق يكتب جملة، لا فقرة — والحدّ يمنع استعلاماً بمئة شرط.
    public const int MaxQueryTokens = 8;

    private const char Tatweel = 'ـ';        // ـ كشيدة: زخرفة خطّية لا صوت لها
    private const char TaMarbuta = 'ة';      // ة
    private const char Ha = 'ه';             // ه
    private const char AlefMaksura = 'ى';    // ى
    private const char Ya = 'ي';             // ي
    private const char Alef = 'ا';           // ا
    private const char AlefWasla = 'ٱ';      // ٱ — لا تتفكّك في Unicode كما تتفكّك أ إ آ، فتُطوى صراحةً
    private const char AlefWavyHamzaAbove = 'ٲ';
    private const char AlefWavyHamzaBelow = 'ٳ';
    private const char ArabicZero = '٠';     // ٠ أرقام عربية-هندية
    private const char ArabicNine = '٩';
    private const char ExtendedZero = '۰';   // ۰ أرقام فارسية/أردية
    private const char ExtendedNine = '۹';

    // ============================================================================
    // يطوي النص إلى صورته القابلة للمطابقة:
    //   • تفكيك Unicode (FormD) ثم حذف كل علامة غير متبوّئة (Mn) — يطوي التشكيل كلّه وهمزات الألف
    //     (أ إ آ ٱ ؤ ئ تتفكّك إلى حرفها + همزة علوية/سفلية) واللهجات اللاتينية (é → e) في خطوة واحدة.
    //   • محارف التنسيق (Cf): ZWJ/ZWNJ وعلامات الاتجاه — تأتي مع اللصق من محرّرات ولا تُرى. تُعامَل **فاصلاً**
    //     لا حذفاً: حذفها يلصق كلمتين فيصيران كلمة واحدة لا يطابقها أحد.
    //   • الكشيدة تُحذف؛ ة ← ه، ى ← ي، وصور الألف التي لا تتفكّك (ٱ ٲ ٳ) ← ا.
    //   • **الهمزة المفردة (ء) تبقى** — حذفها يطوي "ماء" على "ما"، وهو توسيع استدعاء يخلق تصادمات أكثر ممّا يحلّ.
    //     هذا هو طيّ Lucene العربي القياسي نفسه (ArabicNormalizationFilter)، لا اجتهاداً محلياً.
    //   • الأرقام العربية-الهندية والفارسية ← أرقام لاتينية، فـ"٥" و"5" استعلام واحد.
    //   • حالة الأحرف إلى الصغرى، وكل ما ليس حرفاً ولا رقماً يصبح فاصلاً — فالترقيم لا يمنع مطابقة.
    // ============================================================================
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";

        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var pendingSeparator = false;

        foreach (var character in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            // العلامات غير المتبوّئة (تشكيل، همزات الألف بعد التفكيك، لهجات لاتينية) تُحذف بلا فاصل: هي داخل الكلمة.
            if (category == UnicodeCategory.NonSpacingMark) continue;
            if (character == Tatweel) continue;
            if (category == UnicodeCategory.Format)
            {
                pendingSeparator = true;
                continue;
            }

            var folded = character switch
            {
                TaMarbuta => Ha,
                AlefMaksura => Ya,
                AlefWasla or AlefWavyHamzaAbove or AlefWavyHamzaBelow => Alef,
                >= ArabicZero and <= ArabicNine => (char)('0' + (character - ArabicZero)),
                >= ExtendedZero and <= ExtendedNine => (char)('0' + (character - ExtendedZero)),
                _ => char.ToLowerInvariant(character),
            };

            if (char.IsLetterOrDigit(folded))
            {
                // الفاصل يُكتب عند أول حرف بعده، فلا فاصل بادئ ولا لاحق ولا مكرّر.
                if (pendingSeparator && builder.Length > 0) builder.Append(' ');
                pendingSeparator = false;
                builder.Append(folded);
            }
            else
            {
                pendingSeparator = true;
            }
        }

        return builder.ToString();
    }

    // كلمات النص المطبَّع. الاستعلام يُقصّ إلى MaxQueryTokens؛ النص المفهرس يُمرَّر بلا حدّ (take: null).
    public static IReadOnlyList<string> Tokenize(string? text, int? take = null)
    {
        var normalized = Normalize(text);
        if (normalized.Length == 0) return [];

        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (take is null || tokens.Length <= take) return tokens;
        return tokens[..take.Value];
    }
}
