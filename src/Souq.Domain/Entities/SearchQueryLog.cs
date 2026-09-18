using Souq.Domain.Common;
using Souq.Domain.Interfaces;
using Souq.Domain.ValueObjects;

namespace Souq.Domain.Entities;

// ============================================================================
// ما بحث عنه المتسوّقون فعلاً (M13) — الأثر الذي تُحسَّن به مفردات البحث التي بناها M3.
//
// **لماذا صفٌّ لكل بحث لا عدّاد مُجمَّع؟** لأنّ السؤال الذي يخدم التاجر ليس "كم بحثاً جرى" بل "أيّ بحثٍ
// **لم يجد شيئاً**، ومتى". العدّاد المُجمَّع يجيب الأول ويفقد الثاني: بحثٌ صار بلا نتائج بعد نفاد منتج يبدو
// في العدّاد كبحثٍ فاشل دائماً. والتجميع على القراءة رخيص (GROUP BY على عمودٍ مفهرس)، أمّا استعادة الزمن
// بعد تجميعه فمستحيلة.
//
// **ولا بيانات شخصية فيه، بالبناء لا بالاتفاق.** لا معرّف عميل، ولا عنوان IP، ولا معرّف جلسة، ولا رمز
// زائر — ولا موضع لأيٍّ منها في هذا الصفّ. السبب عمليّ لا احتفالي: TD-16 يسجّل أنّ لا سياسةَ حفظٍ لأي
// جدول في النظام، وأنّ قرار الحفظ للبيانات الشخصية يحتاج جواباً قانونيّاً. جدولٌ بلا بيانات شخصية لا
// ينتظر ذلك الجواب — تُحدَّد مدّة حفظه هندسيّاً، وهو ما فعله M13 من أول سطر (SearchLogRetention).
//
// **والمخزَّن هو الصورة المطبَّعة، مع ما كتبه المتسوّق.** المطابقة والتجميع على الأولى (فـ"مكنسة" و"مكنسه"
// بحثٌ واحد لا اثنان، وهو ما يريد التاجر رؤيته)، والعرض بالثانية كي يقرأ التاجر ما كُتب فعلاً — نفس
// القاعدة التي يتبعها SearchSynonym، ولنفس السبب.
// ============================================================================
public class SearchQueryLog : Entity, ITenantOwned
{
    // نفس حدّ المرادف: ما لا يصلح مرادفاً لا يصلح سطراً في السجلّ، والاستعلام الأطول يُقصّ لا يُرفض
    // (السجلّ لا يجوز أن يُفشل بحثاً).
    public const int TermMaxLength = 100;

    public int TenantId { get; private set; }
    public string Culture { get; private set; } = default!;

    // ما كتبه المتسوّق، مقصوصاً لا مرفوضاً.
    public string Term { get; private set; } = default!;

    // الصورة التي طابق بها المحرّك — وهي مفتاح التجميع.
    public string TermNormalized { get; private set; } = default!;

    // عدد ما وجده البحث. صفرٌ هو السطر الذي يستحقّ تصرّفاً.
    public int ResultCount { get; private set; }

    public DateTime SearchedAt { get; private set; }

    public bool FoundNothing => ResultCount == 0;

    private SearchQueryLog() { }

    // ============================================================================
    // يُبنى من معايير البحث نفسها لا من نصٍّ حرّ: التطبيع بـ `SearchText` — **نفس الدالّة** التي يطبّع بها
    // المحرّك نصَّ الكتالوج والاستعلام — فلا تفترق صورة السجلّ عن صورة المطابقة يوماً.
    //
    // يعيد null لما لا يُسجَّل: استعلامٌ فارغ، أو نصٌّ لا يبقى منه بعد التطبيع شيء (رموز وحدها). تسجيلُ
    // هذين يُنتج سطوراً لا تخبر التاجر بشيء ويُثقل الجدول.
    // ============================================================================
    public static SearchQueryLog? For(string? term, int resultCount, string culture, DateTime utcNow)
    {
        var trimmed = term?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;

        var normalized = SearchText.Normalize(trimmed);
        if (string.IsNullOrWhiteSpace(normalized)) return null;

        return new SearchQueryLog
        {
            Culture = culture,
            Term = Truncate(trimmed),
            TermNormalized = Truncate(normalized),
            ResultCount = Math.Max(resultCount, 0),
            SearchedAt = utcNow,
        };
    }

    private static string Truncate(string value) =>
        value.Length <= TermMaxLength ? value : value[..TermMaxLength];
}
