using AwesomeAssertions;
using Souq.Domain.Entities;
using Souq.Domain.Exceptions;

namespace Souq.Domain.Tests;

// ============================================================================
// مرادف بحث المتجر (M3، ADR-0042). القواعد هنا ليست تجميلاً: كل واحدة تمنع صفّاً يبدو صحيحاً للتاجر ولا يفعل
// شيئاً — أو يفعل شيئاً لا يتوقّعه. صفّ بلا أثر أسوأ من رفضٍ واضح، لأنّه يبقى في شاشته ويظنّ أنّه يعمل.
// ============================================================================
public class SearchSynonymTests
{
    [Fact]
    public void الزوج_يُخزَّن_كما_كتبه_التاجر_ومطبَّعاً_معاً()
    {
        var synonym = new SearchSynonym("ar", " جَوّال ", "هَاتِف");

        // ما كتبه التاجر يبقى للعرض في شاشته، مقصوصاً فقط.
        synonym.Term.Should().Be("جَوّال");
        synonym.Expansion.Should().Be("هَاتِف");
        // والصورة المطبَّعة هي ما يُطابَق فعلاً.
        synonym.TermNormalized.Should().Be("جوال");
        synonym.ExpansionNormalized.Should().Be("هاتف");
        synonym.Culture.Should().Be("ar");
    }

    [Theory]
    [InlineData("AR", "ar")]
    [InlineData(" En ", "en")]
    public void اللغة_تُقصّ_وتُصغَّر(string input, string expected) =>
        new SearchSynonym(input, "جوال", "هاتف").Culture.Should().Be(expected);

    [Theory]
    [InlineData("fr")]
    [InlineData("")]
    [InlineData(null)]
    public void لغة_غير_مدعومة_تُرفض(string? culture)
    {
        var act = () => new SearchSynonym(culture, "جوال", "هاتف");
        act.Should().Throw<InvalidSearchSynonymException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void كلمة_فارغة_تُرفض(string? term)
    {
        ((Action)(() => new SearchSynonym("ar", term, "هاتف"))).Should().Throw<InvalidSearchSynonymException>();
        ((Action)(() => new SearchSynonym("ar", "جوال", term))).Should().Throw<InvalidSearchSynonymException>();
    }

    [Fact]
    public void نصّ_بلا_حرف_ولا_رقم_يُرفض()
    {
        // "!!!" صورته المطبَّعة فارغة، فلن يطابق شيئاً أبداً — صفّ بلا أثر.
        var act = () => new SearchSynonym("ar", "!!!", "هاتف");
        act.Should().Throw<InvalidSearchSynonymException>();
    }

    [Fact]
    public void أكثر_من_كلمة_يُرفض_على_كل_طرف()
    {
        // التوسيع على مستوى الكلمة داخل استعلام شروطه AND: عبارة هنا تعني شرطاً داخل شرط بدلالة غامضة.
        ((Action)(() => new SearchSynonym("ar", "جوال ذكي", "هاتف"))).Should().Throw<InvalidSearchSynonymException>();
        ((Action)(() => new SearchSynonym("ar", "جوال", "هاتف ذكي"))).Should().Throw<InvalidSearchSynonymException>();
    }

    [Fact]
    public void كلمة_أطول_من_الحدّ_تُرفض()
    {
        var tooLong = new string('ا', SearchSynonym.TermMaxLength + 1);
        ((Action)(() => new SearchSynonym("ar", tooLong, "هاتف"))).Should().Throw<InvalidSearchSynonymException>();
    }

    [Fact]
    public void كلمة_إلى_نفسها_تُرفض_ولو_اختلف_رسمها()
    {
        // "مكنسة" و"مكنسه" كلمة واحدة بعد التطبيع: الصفّ بلا أثر، ورفضه أصدق من قبوله صامتاً.
        ((Action)(() => new SearchSynonym("ar", "مكنسة", "مكنسة"))).Should().Throw<InvalidSearchSynonymException>();
        ((Action)(() => new SearchSynonym("ar", "مكنسة", "مكنسه"))).Should().Throw<InvalidSearchSynonymException>();
        ((Action)(() => new SearchSynonym("ar", "مَكْنَسَة", "مكنسه"))).Should().Throw<InvalidSearchSynonymException>();
    }

    [Fact]
    public void التعديل_يستبدل_اللغة_والزوج_معاً()
    {
        var synonym = new SearchSynonym("ar", "جوال", "هاتف");

        synonym.Update("en", "cell", "phone");

        synonym.Culture.Should().Be("en");
        synonym.Term.Should().Be("cell");
        synonym.TermNormalized.Should().Be("cell");
        synonym.ExpansionNormalized.Should().Be("phone");
    }

    [Fact]
    public void تعديل_غير_صالح_لا_يترك_الصفّ_نصف_معدَّل()
    {
        // القواعد تُفحص كلّها قبل أن يُكتب أي حقل: صفّ نصف معدَّل أسوأ من تعديل مرفوض.
        var synonym = new SearchSynonym("ar", "جوال", "هاتف");

        ((Action)(() => synonym.Update("ar", "موبايل", "موبايل"))).Should().Throw<InvalidSearchSynonymException>();

        synonym.Term.Should().Be("جوال");
        synonym.Expansion.Should().Be("هاتف");
        synonym.Culture.Should().Be("ar");
    }
}
