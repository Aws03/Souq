using AwesomeAssertions;
using Souq.Domain.Entities;
using Souq.Domain.ValueObjects;

namespace Souq.Domain.Tests;

// ============================================================================
// الصورة المطبَّعة للبحث على صفوف الترجمة (M3، ADR-0042). الدعوى التي يحرسها هذا الملف: **لا مسار يضبط اسماً
// ويترك صورته المطبَّعة متقادمة** — لأنّ Apply هي المسار الوحيد للكتابة، والمُنشئ والتحديث والحذف كلّها تمرّ بها.
// لو كُسر هذا لصار البحث يفوّت منتجاً عُدِّل اسمه، وهو عطل صامت لا يظهر في أي اختبار آخر.
// ============================================================================
public class CatalogSearchProjectionTests
{
    private static Dictionary<string, CatalogText> Texts(string ar, string? en = null, string? descriptionAr = null)
    {
        var texts = new Dictionary<string, CatalogText> { ["ar"] = new(ar, descriptionAr) };
        if (en is not null) texts["en"] = new CatalogText(en);
        return texts;
    }

    private static Product NewProduct(string ar = "مَكْنَسَة كَهْرَبَائِيَّة", string? en = null, string? descriptionAr = null) =>
        new("vacuum", categoryId: 1, Texts(ar, en, descriptionAr), new Money(59.9m, "JOD"));

    private static ProductTranslation Arabic(Product product) => product.Translations.Single(t => t.Culture == "ar");

    [Fact]
    public void إنشاء_منتج_يكتب_الصورة_المطبَّعة_لكل_لغة()
    {
        var product = NewProduct(en: "Vacuum Cleaner");

        Arabic(product).NameNormalized.Should().Be("مكنسه كهرباييه");
        product.Translations.Single(t => t.Culture == "en").NameNormalized.Should().Be("vacuum cleaner");
    }

    [Fact]
    public void تعديل_الاسم_يعيد_كتابة_الصورة_المطبَّعة()
    {
        var product = NewProduct();
        Arabic(product).NameNormalized.Should().Be("مكنسه كهرباييه");

        product.SetTexts(Texts("غَسَّالَة أُطْبَاق"));

        Arabic(product).NameNormalized.Should().Be("غساله اطباق",
            "الصورة المطبَّعة مشتقّة من الاسم، فتعديل الاسم بلا تعديلها يجعل البحث يجد المنتج باسمه القديم");
    }

    [Fact]
    public void إضافة_لغة_وحذفها_تُحدِّثان_الصور_المطبَّعة()
    {
        var product = NewProduct();

        product.SetTexts(Texts("مكنسة", en: "Vacuum"));
        product.Translations.Select(t => t.NameNormalized).Should().BeEquivalentTo(["مكنسه", "vacuum"]);

        product.SetTexts(Texts("مكنسة"));
        product.Translations.Select(t => t.NameNormalized).Should().Equal("مكنسه");
    }

    [Fact]
    public void الوصف_له_صورته_المطبَّعة_والفارغ_منها_null()
    {
        var withDescription = NewProduct(descriptionAr: "شَفْط قويّ — 2000 واط");
        Arabic(withDescription).DescriptionNormalized.Should().Be("شفط قوي 2000 واط");

        var withoutDescription = NewProduct();
        Arabic(withoutDescription).DescriptionNormalized.Should().BeNull(
            "وصف غير موجود صورته null لا \"\" — كي لا يُطابِق استعلامٌ وصفاً لا وجود له");
    }

    [Fact]
    public void الفئة_تتبع_القاعدة_نفسها()
    {
        var category = new Category("home-appliances", Texts("أَجْهِزَة مَنْزِلِيَّة"));

        category.Translations.Single().NameNormalized.Should().Be("اجهزه منزليه");

        category.SetTexts(Texts("إِلْكْتِرُونِيَّات"));
        category.Translations.Single().NameNormalized.Should().Be("الكترونيات");
    }

    [Fact]
    public void إعادة_البناء_لا_تغيّر_شيئاً_لصفّ_صورته_سليمة()
    {
        // مسار التعبئة (SearchIndexBackfill) يعتمد على هذا: لا يحفظ ما لم يتغيّر، فلا يدور على صفوف سليمة.
        var product = NewProduct();
        var translation = Arabic(product);

        translation.RebuildSearchText().Should().BeFalse("الصورة سليمة أصلاً فلا شيء يُكتب");
        translation.NameNormalized.Should().Be("مكنسه كهرباييه");
    }

    [Fact]
    public void إعادة_البناء_تُصلح_صفّاً_سابقاً_للحقل()
    {
        // الحالة الحقيقية: صفّ كُتب قبل وجود العمود، فقرأته EF بقيمته الافتراضية "".
        var product = NewProduct();
        var translation = Arabic(product);
        typeof(CatalogTranslation).GetProperty(nameof(CatalogTranslation.NameNormalized))!
            .SetValue(translation, "");

        translation.RebuildSearchText().Should().BeTrue();
        translation.NameNormalized.Should().Be("مكنسه كهرباييه");
        translation.Name.Should().Be("مَكْنَسَة كَهْرَبَائِيَّة", "التعبئة تبني الصورة ولا تلمس النص نفسه");
    }

    [Fact]
    public void اسم_بلا_حرف_ولا_رقم_صورته_فارغة_بحقّ()
    {
        // ليست حالة فساد: اسم كهذا صورته فارغة فعلاً، ومسار التعبئة يجب ألّا يعدّها عملاً ناقصاً فيدور عليها أبداً.
        var product = NewProduct(ar: "!!!");

        Arabic(product).NameNormalized.Should().BeEmpty();
        Arabic(product).RebuildSearchText().Should().BeFalse();
    }
}
