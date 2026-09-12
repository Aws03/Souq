using AwesomeAssertions;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Exceptions;
using Souq.Domain.ValueObjects;

namespace Souq.Domain.Tests;

// تجمّع المنتج (المرحلة 5): النصوص لكل لغة، البيع عبر متغيّر افتراضي واحد (السعر وسعر المقارنة وSKU)، دورة حياة
// بلا حذف، ومعرض صور مرتّب محدود. المخزون في وحدة Inventory منذ المرحلة 6 (InventoryItemTests).
public class ProductTests
{
    private static Dictionary<string, CatalogText> Texts(string ar = "سماعات لاسلكية", string? en = null)
    {
        var texts = new Dictionary<string, CatalogText> { ["ar"] = new(ar, "صوت نقي") };
        if (en is not null) texts["en"] = new CatalogText(en);
        return texts;
    }

    private static Product NewProduct(ProductStatus status = ProductStatus.Active) =>
        new("wireless-headphones", categoryId: 1, Texts(), new Money(59.9m, "JOD"), status);

    [Fact]
    public void الجديد_نشط_افتراضياً_بمتغيّر_افتراضي_واحد_يحمل_السعر()
    {
        var product = NewProduct();

        product.IsActive.Should().BeTrue();
        product.Variants.Should().ContainSingle().Which.IsDefault.Should().BeTrue();
        product.Price.Should().Be(new Money(59.9m, "JOD"));
        product.CompareAtPrice.Should().BeNull();
        product.NameIn("ar").Should().Be("سماعات لاسلكية");
    }

    [Fact]
    public void لا_يُنشأ_منتج_مؤرشفاً()
    {
        var act = () => NewProduct(status: ProductStatus.Archived);

        act.Should().Throw<InvalidProductDataException>();
    }

    [Fact]
    public void النصوص_بلغات_مدعومة_وتُستبدل_كاملة()
    {
        var product = NewProduct();

        product.SetTexts(Texts("سماعات", en: "Headphones"));
        product.Translations.Select(t => t.Culture).Should().BeEquivalentTo(["ar", "en"]);
        product.NameIn("en").Should().Be("Headphones");
        product.NameIn("fr").Should().Be("سماعات");            // لغة غير موجودة ⇒ العربية

        product.SetTexts(new Dictionary<string, CatalogText> { ["en"] = new("Only English") });
        product.Translations.Should().ContainSingle().Which.Culture.Should().Be("en");

        ((Action)(() => product.SetTexts(new Dictionary<string, CatalogText> { ["fr"] = new("Casque") })))
            .Should().Throw<InvalidProductDataException>();
        ((Action)(() => product.SetTexts(new Dictionary<string, CatalogText>()))).Should().Throw<InvalidProductDataException>();
        ((Action)(() => product.SetTexts(new Dictionary<string, CatalogText> { ["ar"] = new("  ") })))
            .Should().Throw<InvalidProductDataException>();
    }

    [Theory]
    [InlineData("Wireless Headphones")]
    [InlineData("-bad")]
    [InlineData("a")]
    [InlineData("سماعات")]
    public void معرّف_رابط_غير_صالح_يُرفض(string slug)
    {
        var act = () => NewProduct().SetSlug(slug);

        act.Should().Throw<InvalidProductDataException>();
    }

    [Fact]
    public void سعر_المقارنة_أعلى_من_السعر_وبعملته_وSKU_يُطبَّع()
    {
        var product = NewProduct();

        product.SetPricing(new Money(40m, "JOD"), new Money(59.9m, "JOD"), " hp-01 ");

        product.Price.Amount.Should().Be(40m);
        product.CompareAtPrice!.Amount.Should().Be(59.9m);
        product.DefaultVariant.IsOnSale.Should().BeTrue();
        product.Sku.Should().Be("HP-01");

        ((Action)(() => product.SetPricing(new Money(40m, "JOD"), new Money(40m, "JOD"), null))).Should().Throw<InvalidProductDataException>();
        ((Action)(() => product.SetPricing(new Money(40m, "JOD"), new Money(50m, "USD"), null))).Should().Throw<InvalidProductDataException>();
        ((Action)(() => product.SetPricing(new Money(0m, "JOD"), null, null))).Should().Throw<InvalidProductDataException>();
        ((Action)(() => product.SetPricing(new Money(40m, "JOD"), null, "has space"))).Should().Throw<InvalidProductDataException>();
    }

    [Fact]
    public void دورة_الحياة_مسودّة_نشط_مؤرشف_واستعادة()
    {
        var product = NewProduct(status: ProductStatus.Draft);
        product.IsActive.Should().BeFalse();       // المسودّة لا تُعرض ولا تُباع

        product.ChangeStatus(ProductStatus.Active);
        product.IsActive.Should().BeTrue();

        product.Archive();
        product.Status.Should().Be(ProductStatus.Archived);
        product.IsActive.Should().BeFalse();

        product.ChangeStatus(ProductStatus.Draft);        // استعادة مسودّةً
        product.Status.Should().Be(ProductStatus.Draft);
    }

    // R-07: الفئة المعطّلة تُخفي منتجاتها من المتجر، وكان الشراء يفحص حالة المنتج وحدها — فيبقى منتجها قابلاً للشراء بمعرّفه.
    [Fact]
    public void القابلية_للبيع_تتطلّب_منتجاً_نشطاً_في_فئة_مفعّلة()
    {
        var category = new Category("electronics", new Dictionary<string, CatalogText> { ["ar"] = new("إلكترونيات") });
        var product = WithCategory(NewProduct(), category);

        product.IsSellable.Should().BeTrue();

        category.Deactivate();
        product.IsSellable.Should().BeFalse("فئة معطّلة لا تُباع منتجاتها");
        product.IsActive.Should().BeTrue("حالة المنتج نفسها لم تتغيّر — الإخفاء قرار الفئة");

        category.Activate();
        product.ChangeStatus(ProductStatus.Draft);
        product.IsSellable.Should().BeFalse("المسودّة لا تُباع ولو كانت فئتها مفعّلة");

        NewProduct().IsSellable.Should().BeFalse("بلا فئة محمّلة لا بيع — المستودع يحمّلها دائماً");
    }

    private static Product WithCategory(Product product, Category category)
    {
        typeof(Product).GetProperty(nameof(Product.Category))!.GetSetMethod(nonPublic: true)!.Invoke(product, [category]);
        return product;
    }

    [Fact]
    public void معرض_الصور_مرتّب_ومحدود_والترتيب_يشمل_كل_صورة_مرّة()
    {
        var product = NewProduct();
        product.AddImage("/uploads/tenants/1/images/a.png");
        product.AddImage("/uploads/tenants/1/images/b.png");

        product.PrimaryImageUrl.Should().EndWith("a.png");
        product.Images.Select(i => i.SortOrder).Should().Equal(0, 1);
        ((Action)(() => product.ReorderImages([0]))).Should().Throw<InvalidProductDataException>();

        for (var i = product.Images.Count; i < Product.MaxImages; i++) product.AddImage($"/uploads/x{i}.png");
        ((Action)(() => product.AddImage("/uploads/one-too-many.png"))).Should().Throw<InvalidProductDataException>();
        ((Action)(() => product.AddImage(" "))).Should().Throw<InvalidProductDataException>();
    }
}

// شجرة الفئات: لا نقل تحت النفس أو تحت فرع، ولا أعمق من الحدّ؛ النصوص والمعرّف بقواعد الكتالوج نفسها.
public class CategoryTests
{
    private static Category NewCategory(int id = 10)
    {
        var category = new Category("electronics", new Dictionary<string, CatalogText> { ["ar"] = new("إلكترونيات") });
        typeof(Souq.Domain.Common.Entity).GetProperty("Id")!.SetValue(category, id);
        return category;
    }

    [Fact]
    public void الجديدة_مفعّلة_بلا_أب_واسمها_من_ترجمتها()
    {
        var category = NewCategory();

        category.IsActive.Should().BeTrue();
        category.ParentId.Should().BeNull();
        category.Name.Should().Be("إلكترونيات");
    }

    [Fact]
    public void لا_تُنقل_تحت_نفسها_ولا_تحت_أحد_فروعها()
    {
        var category = NewCategory(id: 10);

        ((Action)(() => category.MoveTo(10, [10]))).Should().Throw<InvalidCategoryParentException>();
        // الأب المقترح 30 فرع من 10 (سلسلته: 30 ⇒ 20 ⇒ 10).
        ((Action)(() => category.MoveTo(30, [30, 20, 10]))).Should().Throw<InvalidCategoryParentException>();

        category.MoveTo(40, [40]);
        category.ParentId.Should().Be(40);
        category.MoveTo(null, []);
        category.ParentId.Should().BeNull();
    }

    [Fact]
    public void الشجرة_لا_تتجاوز_الحدّ_مع_فروع_الفئة_المنقولة()
    {
        var category = NewCategory();

        // أب على العمق 4 + ورقة = 5 (مسموح)؛ + فرع بعمق 2 = 6 (مرفوض).
        category.MoveTo(4, [4, 3, 2, 1], subtreeHeight: 1);
        ((Action)(() => category.MoveTo(4, [4, 3, 2, 1], subtreeHeight: 2))).Should().Throw<InvalidCategoryParentException>();
    }

    [Fact]
    public void الترتيب_والمعرّف_بقواعدهما()
    {
        var category = NewCategory();

        ((Action)(() => category.SetSortOrder(-1))).Should().Throw<InvalidCategoryException>();
        ((Action)(() => category.SetSlug("Bad Slug"))).Should().Throw<InvalidCategoryException>();
        category.SetSlug("Home-Decor");
        category.Slug.Should().Be("home-decor");
    }
}
