using AwesomeAssertions;
using Souq.Domain.ValueObjects;

namespace Souq.Domain.Tests;

// ============================================================================
// مسافة التحرير المحدودة (M3، ADR-0042): أساس "هل قصد المتسوّق كلمة أخرى؟". الحالة التي بُنيت لأجلها صريحة في
// أول اختبار هنا: مكلسة → مكنسة. الحدّ الأعلى جزء من العقد، لا تفصيلة تنفيذ: فوقه تُعَدّ الكلمة كلمة أخرى لا خطأً.
// ============================================================================
public class SearchDistanceTests
{
    [Fact]
    public void الحالة_المقصودة_مكلسة_خطأ_مطبعي_عن_مكنسة()
    {
        // إبدال حرف واحد (ل ← ن) — وهي الحالة التي يسمّيها M3 بالاسم في SouqMasterPlan.md.
        SearchDistance.Between("مكلسه", "مكنسه").Should().Be(1);
        SearchDistance.IsTypoOf("مكنسه", "مكلسه").Should().BeTrue();
    }

    [Theory]
    // صفر: متطابقتان بعد التطبيع.
    [InlineData("مكنسه", "مكنسه", 0)]
    // إبدال حرف.
    [InlineData("مكلسه", "مكنسه", 1)]
    [InlineData("vacuum", "vacuun", 1)]
    // حذف حرف.
    [InlineData("مكنسه", "مكسه", 1)]
    // إدراج حرف.
    [InlineData("مكنسه", "مكننسه", 1)]
    // تبديل جارَين: خطوة واحدة لا اثنتان — وهو سبب اختيار Damerau على Levenshtein.
    [InlineData("مكسنه", "مكنسه", 1)]
    [InlineData("vcauum", "vacuum", 1)]
    // مسافة 2.
    [InlineData("مكلسة", "مكنسه", 2)]
    [InlineData("vacum", "vacuum", 1)]
    [InlineData("vaccum", "vacuum", 1)]
    public void المسافة_تُحسب_لما_هو_داخل_الحدّ(string a, string b, int expected) =>
        SearchDistance.Between(a, b).Should().Be(expected);

    [Theory]
    // فوق الحدّ: كلمة أخرى لا خطأ مطبعي.
    [InlineData("مكنسه", "غساله")]
    [InlineData("مكنسه", "مكيف")]
    [InlineData("vacuum", "washer")]
    // فرق الطول وحده يُخرجها.
    [InlineData("مكنسه", "مكنسه كهربائيه")]
    [InlineData("a", "abcdef")]
    public void ما_يتجاوز_الحدّ_يعود_Beyond(string a, string b)
    {
        SearchDistance.Between(a, b).Should().Be(SearchDistance.Beyond);
        SearchDistance.IsTypoOf(a, b).Should().BeFalse();
    }

    [Fact]
    public void تبديل_الجارَين_خطوة_واحدة_لا_اثنتان()
    {
        // لو حُسب إبدالين لصارت المسافة 2، وكلمتان بتبديلين متجاورين لخرجتا عن الحدّ تماماً.
        SearchDistance.Between("مكسنه", "مكنسه", maxDistance: 1).Should().Be(1);
        SearchDistance.Between("ab", "ba", maxDistance: 1).Should().Be(1);
    }

    [Fact]
    public void حدّ_صفر_يعني_تطابقاً_تامّاً()
    {
        SearchDistance.Between("مكنسه", "مكنسه", maxDistance: 0).Should().Be(0);
        SearchDistance.Between("مكنسه", "مكلسه", maxDistance: 0).Should().Be(SearchDistance.Beyond);
    }

    [Theory]
    [InlineData(null, null, 0)]
    [InlineData("", "", 0)]
    [InlineData("", "ab", 2)]
    [InlineData("ab", "", 2)]
    public void الفارغ_والمعدوم_يُعامَلان_كنصّ_فارغ(string? a, string? b, int expected) =>
        SearchDistance.Between(a, b).Should().Be(expected);

    [Fact]
    public void الفارغ_مقابل_ما_يتجاوز_الحدّ_يعود_Beyond() =>
        SearchDistance.Between("", "abc").Should().Be(SearchDistance.Beyond);

    [Fact]
    public void المسافة_متناظرة()
    {
        // تُستدعى في الاتجاهين على المسار (كلمة الكتالوج مقابل كلمة الاستعلام والعكس)، فالتناظر جزء من العقد.
        (string a, string b)[] pairs =
        [
            ("مكلسه", "مكنسه"), ("مكسنه", "مكنسه"), ("vacuum", "vaccum"),
            ("مكنسه", "غساله"), ("abc", "abcde"), ("", "ab"),
        ];

        foreach (var (a, b) in pairs)
            SearchDistance.Between(a, b).Should().Be(SearchDistance.Between(b, a), $"{a} ↔ {b}");
    }

    [Fact]
    public void المسافة_لا_تتأثّر_بطول_الكلمات_المتباعدة_جدّاً()
    {
        // الحدّ الأدنى من فرق الطول يُنهي المقارنة بلا حساب مصفوفة — يهمّ لأنّ المسار يقارن كلمة واحدة بآلاف الكلمات.
        var longWord = new string('a', 500);

        SearchDistance.Between("ab", longWord).Should().Be(SearchDistance.Beyond);
        SearchDistance.Between(longWord, longWord).Should().Be(0);
    }

    [Fact]
    public void التطبيع_يسبق_المسافة_في_الاستخدام_الحقيقي()
    {
        // "مكنسة" و"مكنسه" مسافتهما 1 كنصّ خام، وصفر بعد التطبيع — فالمسار يطبّع أولاً ثم يقيس، وإلا استُهلك
        // نصف الحدّ على فرق رسم لا على خطأ فعلي.
        SearchDistance.Between("مكنسة", "مكنسه").Should().Be(1);
        SearchDistance.Between(SearchText.Normalize("مكنسة"), SearchText.Normalize("مكنسه")).Should().Be(0);

        // وبه يبقى الحدّ كلّه متاحاً للخطأ الحقيقي: "مكلسة" (بتاء مربوطة وإبدال حرف) تبقى داخل الحدّ.
        SearchDistance.Between(SearchText.Normalize("مكلسة"), SearchText.Normalize("مكنسه")).Should().Be(1);
    }
}
