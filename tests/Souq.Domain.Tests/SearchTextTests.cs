using AwesomeAssertions;
using Souq.Domain.ValueObjects;

namespace Souq.Domain.Tests;

// ============================================================================
// تطبيع نص البحث (M3، ADR-0042). هذا الملف هو المواصفة التنفيذية للتطبيع: كل سطر هنا سلوك يعتمد عليه مسار البحث،
// لأنّ الدالّة تُطبَّق على النص المفهرس ونصّ الاستعلام معاً — فأي تغيير فيها يُبطل الفهرس المخزَّن ويحتاج إعادة بناء.
// ============================================================================
public class SearchTextTests
{
    [Theory]
    // التشكيل يُحذف: المتسوّق لا يكتبه والتاجر قد يكتبه (أو العكس).
    [InlineData("مَكْنَسَة", "مكنسه")]
    [InlineData("مُكَيِّف", "مكيف")]
    // الكشيدة زخرفة خطّية.
    [InlineData("مكــنسة", "مكنسه")]
    // صور الألف كلها ألف واحدة.
    [InlineData("أحمر", "احمر")]
    [InlineData("إضاءة", "اضاءه")]
    [InlineData("آلة", "اله")]
    // التاء المربوطة هاء، والألف المقصورة ياء.
    [InlineData("غسالة", "غساله")]
    [InlineData("مقلاه", "مقلاه")]
    [InlineData("كبرى", "كبري")]
    [InlineData("مستوى", "مستوي")]
    // الهمزة على الواو والياء تتفكّك إلى حرفها (ؤ ← و، ئ ← ي) — طيّ Lucene العربي نفسه.
    [InlineData("مسؤول", "مسوول")]
    [InlineData("رئيسي", "رييسي")]
    [InlineData("كهربائية", "كهرباييه")]
    // الهمزة المفردة **تبقى**: حذفها يطوي "ماء" على "ما" فيخلق تصادمات.
    [InlineData("ماء", "ماء")]
    [InlineData("كهرباء", "كهرباء")]
    public void التطبيع_يطوي_صور_الحروف_العربية(string input, string expected) =>
        SearchText.Normalize(input).Should().Be(expected);

    [Theory]
    [InlineData("Vacuum Cleaner", "vacuum cleaner")]
    [InlineData("VACUUM", "vacuum")]
    // اللهجات اللاتينية تُطوى بالتفكيك نفسه الذي يطوي التشكيل العربي.
    [InlineData("Café", "cafe")]
    [InlineData("naïve", "naive")]
    [InlineData("Crème Brûlée", "creme brulee")]
    public void التطبيع_يطوي_حالة_الأحرف_واللهجات_اللاتينية(string input, string expected) =>
        SearchText.Normalize(input).Should().Be(expected);

    [Theory]
    // الأرقام العربية-الهندية والفارسية استعلام واحد مع اللاتينية.
    [InlineData("مكيف ١٢٠٠٠ وحدة", "مكيف 12000 وحده")]
    [InlineData("۵ لتر", "5 لتر")]
    [InlineData("5 Litre", "5 litre")]
    public void التطبيع_يوحّد_الأرقام(string input, string expected) =>
        SearchText.Normalize(input).Should().Be(expected);

    [Theory]
    // الترقيم فاصل لا مانع مطابقة.
    [InlineData("مكنسة، كهربائية", "مكنسه كهرباييه")]
    [InlineData("Wi-Fi", "wi fi")]
    [InlineData("(جديد) 100%", "جديد 100")]
    [InlineData("A/B", "a b")]
    // المسافات تُجمَع ولا فاصل بادئ ولا لاحق.
    [InlineData("  مكنسة   كهربائية  ", "مكنسه كهرباييه")]
    [InlineData("!!!مكنسة!!!", "مكنسه")]
    public void التطبيع_يجعل_الترقيم_فاصلاً_ويجمع_المسافات(string input, string expected) =>
        SearchText.Normalize(input).Should().Be(expected);

    [Theory]
    // محارف التنسيق غير المرئية تأتي مع اللصق من محرّرات ومن مواقع أخرى.
    [InlineData("مكنسة‌كهربائية", "مكنسه كهرباييه")]
    [InlineData("‏مكنسة‎", "مكنسه")]
    public void التطبيع_يعامل_محارف_التنسيق_غير_المرئية_فاصلاً(string input, string expected) =>
        SearchText.Normalize(input).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!")]
    [InlineData("؟،.")]
    public void التطبيع_يعطي_نصاً_فارغاً_لما_لا_يحمل_حرفاً_ولا_رقماً(string? input) =>
        SearchText.Normalize(input).Should().BeEmpty();

    [Fact]
    public void التطبيع_لا_يُطيل_نص_العربية_والإنجليزية_أبداً()
    {
        // الثابت الذي تعتمد عليه أعمدة الصورة المطبَّعة: طولها كطول أعمدة أصلها، فلا قصّ صامت عند الإدراج.
        string[] samples =
        [
            "مَكْنَسَة كَهْرَبَائِيَّة", "أإآؤئةى", "مكــــنسة", "Crème Brûlée café naïve",
            "مكيف ١٢٣٤٥٦٧٨٩٠", "(جديد) 100% — خصم!", new string('أ', 200), new string('é', 200),
        ];

        foreach (var sample in samples)
            SearchText.Normalize(sample).Length.Should().BeLessThanOrEqualTo(sample.Length,
                $"الصورة المطبَّعة تُخزَّن في عمود بطول عمود أصلها: {sample}");
    }

    [Fact]
    public void التطبيع_مُستقرّ_عند_إعادة_تطبيقه()
    {
        // يهمّ لأنّ نصّ الاستعلام قد يُطبَّع مرتين على المسار (مرادف مخزَّن مطبَّعاً ثم يُطبَّع مع الاستعلام).
        string[] samples = ["مَكْنَسَة كَهْرَبَائِيَّة", "Crème Brûlée", "مكيف ١٢٠٠٠ وحدة", "Wi-Fi"];

        foreach (var sample in samples)
        {
            var once = SearchText.Normalize(sample);
            SearchText.Normalize(once).Should().Be(once, $"التطبيع يجب أن يكون مُتحقِّقاً من ثبوته: {sample}");
        }
    }

    [Fact]
    public void الكلمات_تُقسَّم_على_المسافات_بعد_التطبيع()
    {
        SearchText.Tokenize("مَكْنَسَة كَهْرَبَائِيَّة، سامسونج").Should()
            .Equal("مكنسه", "كهرباييه", "سامسونج");
        SearchText.Tokenize("  ").Should().BeEmpty();
        SearchText.Tokenize(null).Should().BeEmpty();
    }

    [Fact]
    public void كلمات_الاستعلام_تُقصّ_إلى_الحدّ_الأعلى()
    {
        var manyWords = string.Join(' ', Enumerable.Range(1, SearchText.MaxQueryTokens + 5).Select(i => $"كلمه{i}"));

        SearchText.Tokenize(manyWords, take: SearchText.MaxQueryTokens).Should().HaveCount(SearchText.MaxQueryTokens);
        SearchText.Tokenize(manyWords).Should().HaveCount(SearchText.MaxQueryTokens + 5, "النص المفهرس يُمرَّر بلا حدّ");
    }

    [Fact]
    public void استعلام_المتسوّق_وعنوان_التاجر_يتقابلان_بعد_التطبيع()
    {
        // الحالة التي يوجد التطبيع لأجلها: التاجر كتب الاسم مشكَّلاً بتاء مربوطة، والمتسوّق كتبه بلا تشكيل بهاء.
        SearchText.Normalize("مِكْنَسَة كَهْرَبَائِيَّة").Should().Be(SearchText.Normalize("مكنسه كهربائيه"));
        SearchText.Normalize("إضاءة LED").Should().Be(SearchText.Normalize("اضاءه led"));
    }
}
