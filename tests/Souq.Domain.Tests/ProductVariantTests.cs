using AwesomeAssertions;
using Souq.Domain.Common;
using Souq.Domain.Entities;
using Souq.Domain.Exceptions;
using Souq.Domain.ValueObjects;

namespace Souq.Domain.Tests;

// المتغيّرات (ProductVariants.md، V1 وV2): أيّ متغيّر يُشترى حين لا يُسمّى، ومتى يُشترى متغيّر مسمّى، والتعطيل بدل الحذف مع
// افتراضي نشط دائماً. متغيّر ثانٍ يُنشأ كما يُنشئه المدير: خيار "المقاس" أولاً، ثم متغيّر بقيمة منه (قواعد الخيارات في ProductOptionTests).
public class ProductVariantTests
{
    [Fact]
    public void الافتراضي_ينتقل_لمتغيّر_نشط_فقط_ثم_يُعطَّل_القديم()
    {
        var product = NewProduct();
        var second = Second(product);

        product.DeactivateVariant(second.Id);
        ((Action)(() => product.SetDefaultVariant(second.Id))).Should().Throw<InvalidProductVariantException>()
            .Which.Code.Should().Be("DefaultVariantMustBeActive");

        product.ActivateVariant(second.Id);
        product.SetDefaultVariant(second.Id);
        product.DeactivateVariant(11);

        product.DefaultVariant.Should().BeSameAs(second);
        product.Variants.Count(v => v.IsDefault).Should().Be(1);
        product.Price.Should().Be(new Money(25, "JOD"), "سعر المنتج في القوائم اختصار الافتراضي الجديد");
        product.ImplicitVariant.Should().BeSameAs(second);
    }

    [Fact]
    public void تعديل_متغيّر_بقواعد_التسعير_وSKU_غير_مكرّر_داخل_المنتج()
    {
        var product = NewProduct();
        product.SetPricing(new Money(20, "JOD"), null, "SHIRT-S");
        var second = Second(product);

        product.UpdateVariant(second.Id, new Money(27, "JOD"), new Money(30, "JOD"), " shirt-xl ");
        (second.Price, second.CompareAtPrice, second.Sku).Should().Be((new Money(27, "JOD"), new Money(30, "JOD"), "SHIRT-XL"));

        ((Action)(() => product.UpdateVariant(second.Id, new Money(27, "JOD"), null, "shirt-s"))).Should()
            .Throw<InvalidProductVariantException>().Which.Code.Should().Be("DuplicateVariantSku");
        ((Action)(() => product.UpdateVariant(second.Id, new Money(27, "JOD"), new Money(20, "JOD"), null))).Should()
            .Throw<InvalidProductDataException>();
        ((Action)(() => product.UpdateVariant(second.Id, new Money(27, "KWD"), null, null))).Should()
            .Throw<InvalidProductDataException>();
        second.Sku.Should().Be("SHIRT-XL", "رفض التعديل لا يغيّر شيئاً");
    }

    [Fact]
    public void سعر_المنتج_من_نموذجه_يُرفض_لمنتج_بخيارات_إلا_إن_لم_يتغيّر()
    {
        var product = NewProduct();
        product.SetPricing(new Money(20, "JOD"), null, "SHIRT-S");
        Second(product);

        product.SetPricing(new Money(20.000m, "JOD"), null, " shirt-s ");
        ((Action)(() => product.SetPricing(new Money(21, "JOD"), null, "SHIRT-S"))).Should()
            .Throw<InvalidProductVariantException>().Which.Code.Should().Be("ProductHasVariants");
        ((Action)(() => product.SetPricing(new Money(20, "JOD"), null, "OTHER"))).Should()
            .Throw<InvalidProductVariantException>().Which.Code.Should().Be("ProductHasVariants");
        product.Price.Should().Be(new Money(20, "JOD"));
    }

    private static Product NewProduct(bool categoryActive = true)
    {
        var product = new Product("shirt", categoryId: 1,
            new Dictionary<string, CatalogText> { ["ar"] = new("قميص") }, new Money(20, "JOD"));
        var category = new Category("clothes", new Dictionary<string, CatalogText> { ["ar"] = new("ملابس") });
        if (!categoryActive) category.Deactivate();
        typeof(Product).GetProperty(nameof(Product.Category))!.GetSetMethod(nonPublic: true)!.Invoke(product, [category]);
        WithId(product.DefaultVariant, 11);
        return product;
    }

    private static ProductVariant Second(Product product, decimal price = 25, int id = 12)
    {
        product.SetOptions([new ProductOptionDefinition(null, new Dictionary<string, string> { ["ar"] = "المقاس" },
            [new(null, new Dictionary<string, string> { ["ar"] = "S" }), new(null, new Dictionary<string, string> { ["ar"] = "L" })],
            ExistingVariantsValue: 0)]);
        var values = product.Options.Single().Values.OrderBy(v => v.Position).ToList();
        WithId(product.Options.Single(), id * 100);
        WithId(values[0], id * 100 + 1);
        WithId(values[1], id * 100 + 2);
        return WithId(product.AddVariant([values[1].Id], new Money(price, "JOD"), sku: "SHIRT-L"), id);
    }

    private static T WithId<T>(T entity, int id) where T : Entity
    {
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(entity, id);
        return entity;
    }

    [Fact]
    public void منتج_بسيط_متغيّره_الضمني_هو_الافتراضي_النشط()
    {
        var product = NewProduct();

        product.DefaultVariant.IsActive.Should().BeTrue();
        product.ImplicitVariant.Should().BeSameAs(product.DefaultVariant);
        product.CanSell(product.DefaultVariant).Should().BeTrue();
    }

    [Fact]
    public void بأكثر_من_متغيّر_نشط_لا_يُفترض_متغيّر()
    {
        var product = NewProduct();
        Second(product);

        product.ImplicitVariant.Should().BeNull("الاختيار صريح حين للمنتج أكثر من متغيّر نشط (P-08c)");
    }

    [Fact]
    public void بعد_تعطيل_الآخر_يعود_الافتراضي_ضمنياً()
    {
        var product = NewProduct();
        var second = Second(product);

        product.DeactivateVariant(second.Id);

        second.IsActive.Should().BeFalse();
        product.CanSell(second).Should().BeFalse();
        product.ImplicitVariant.Should().BeSameAs(product.DefaultVariant);

        product.ActivateVariant(second.Id);
        product.CanSell(second).Should().BeTrue();
    }

    [Fact]
    public void الافتراضي_لا_يُعطَّل_والمتغيّر_الغريب_غير_موجود()
    {
        var product = NewProduct();

        ((Action)(() => product.DeactivateVariant(11))).Should().Throw<InvalidProductVariantException>()
            .Which.Code.Should().Be("DefaultVariantCannotBeDeactivated");
        ((Action)(() => product.DeactivateVariant(99))).Should().Throw<InvalidProductVariantException>()
            .Which.Code.Should().Be("VariantNotFound");
        product.DefaultVariant.IsActive.Should().BeTrue();
    }

    [Fact]
    public void متغيّر_منتج_آخر_لا_يُعثر_عليه_ولا_يُباع_هنا()
    {
        var product = NewProduct();
        var other = NewProduct();
        var foreign = Second(other, id: 77);

        product.FindVariant(77).Should().BeNull();
        product.FindVariant(0).Should().BeNull();
        product.CanSell(foreign).Should().BeFalse();
    }

    [Fact]
    public void منتج_غير_قابل_للبيع_لا_يبيع_أي_متغيّر()
    {
        var product = NewProduct(categoryActive: false);

        product.CanSell(product.DefaultVariant).Should().BeFalse();
    }

    [Fact]
    public void لكل_متغيّر_سعره_بقواعد_التسعير_نفسها_وعملة_المنتج()
    {
        var product = NewProduct();

        Second(product, price: 25).Price.Should().Be(new Money(25, "JOD"));
        product.SetOptions([new ProductOptionDefinition(product.Options.Single().Id, new Dictionary<string, string> { ["ar"] = "المقاس" },
            [.. product.Options.Single().Values.Select(v => new ProductOptionValueDefinition(v.Id, new Dictionary<string, string> { ["ar"] = v.NameIn("ar") })),
             new(null, new Dictionary<string, string> { ["ar"] = "XL" })])]);
        var xl = WithId(product.Options.Single().Values.Single(v => v.Id == 0), 1299);
        ((Action)(() => product.AddVariant([xl.Id], new Money(0, "JOD")))).Should().Throw<InvalidProductDataException>();
        ((Action)(() => product.AddVariant([xl.Id], new Money(10, "KWD")))).Should().Throw<InvalidProductDataException>();
        product.Price.Should().Be(new Money(20, "JOD"), "سعر المنتج يبقى اختصار متغيّره الافتراضي");
    }
}

// ============================================================================
// تكلفة الوحدة (C11) — وكل ما فيها مبنيّ على تمييز واحد: **الغياب ليس صفراً**. تاجرٌ لا يمسك
// تكاليفه يجب أن يُقال له "لا نعرف"، لا أن يُنسب إليه ربحٌ كامل.
// ============================================================================
public class ProductCostTests
{
    [Fact]
    public void التكلفة_اختيارية_وتُمسح_بالغياب()
    {
        var product = SimpleProduct();

        product.DefaultVariant.Cost.Should().BeNull("منتجٌ جديد لا تُعرف تكلفته — لا تساوي صفراً");

        product.SetCost(new Money(12.5m, "JOD"));
        product.DefaultVariant.Cost!.Amount.Should().Be(12.5m);
        product.DefaultVariant.Cost.Currency.Should().Be("JOD", "العملة من السعر، فلا تتناقض معه أبداً");

        product.SetCost(null);
        product.DefaultVariant.Cost.Should().BeNull("رقمٌ أُدخل خطأً يجب أن يكون محوُه ممكناً");
    }

    [Fact]
    public void تكلفة_سالبة_تُرفض_وصفر_يُقبل()
    {
        var product = SimpleProduct();

        // `Money` هو من يرفض السالب، لا `SetCost` — فلا فحص مكرّر في المجال.
        ((Action)(() => product.SetCost(new Money(-1m, "JOD")))).Should().Throw<InvalidMoneyException>();

        // صفر قيمة مشروعة: عيّنة مجّانية، أو هديّة ترويجية.
        product.SetCost(new Money(0m, "JOD"));
        product.DefaultVariant.Cost!.Amount.Should().Be(0m);
    }

    [Fact]
    public void تكلفة_أعلى_من_السعر_تُقبل()
    {
        // البيع بخسارة قرار تجاري مشروع (منتج جاذب، تصفية مخزون). رفضُه كان سيمنع التاجر من
        // تسجيل حقيقة متجره — وهامشٌ سالب معلومةٌ يحتاجها، لا خطأ إدخال.
        var product = SimpleProduct();

        product.SetCost(new Money(50m, "JOD"));

        product.DefaultVariant.Cost!.Amount.Should().Be(50m);
        product.Price.Amount.Should().Be(20m);
    }

    [Fact]
    public void منتج_بخيارات_لا_تُضبط_تكلفته_من_نموذج_المنتج()
    {
        // لو مرّت من هنا لكُتبت على المتغيّر الافتراضي وحده — تعديلٌ لم يقصده المدير على واحدٍ من عدّة.
        var product = SimpleProduct();
        WithOption(product);

        ((Action)(() => product.SetCost(new Money(5m, "JOD"))))
            .Should().Throw<InvalidProductVariantException>().Which.Code.Should().Be("ProductHasVariants");
    }

    [Fact]
    public void تكلفة_سطر_الطلب_لقطة_لا_تتغيّر_بتغيّر_تكلفة_المتغيّر()
    {
        // ============================================================================
        // هذا هو سبب وجود العمود أصلاً: بغير اللقطة يُحسب هامش العام الماضي من تكلفة اليوم،
        // فيتحرّك ربحٌ مُبلَّغ عنه كلّما صحّح التاجر رقماً. تقريرٌ يتغيّر بأثر رجعي ليس تقريراً.
        // ============================================================================
        var order = new Order(customerId: 1, "عمّان", "JOD");
        order.AddItem(productId: 1, variantId: 11, "قميص", new Money(20m, "JOD"), 2,
            unitCost: new Money(12m, "JOD"));

        var line = order.Items.Single();
        line.UnitCost!.Amount.Should().Be(12m);
        line.UnitCost.Currency.Should().Be("JOD");

        // تتغيّر تكلفة المتغيّر اليوم — والسطر لا يتأثّر: هو نسخة مجمّدة لا مرجع.
        var product = SimpleProduct();
        product.SetCost(new Money(99m, "JOD"));
        line.UnitCost.Amount.Should().Be(12m);
    }

    [Fact]
    public void سطر_بلا_تكلفة_يبقى_بلا_تكلفة()
    {
        // الأسطر السابقة للعمود، والمنتجات التي لم تُدخَل تكلفتها: تبقى فارغة ولا تُملأ بقيمة
        // اليوم — كما لا يُملأ `VariantLabel` للأسطر التاريخية. التقرير يقول كم يعرف بدل أن يخترع.
        var order = new Order(customerId: 1, "عمّان", "JOD");
        order.AddItem(productId: 1, variantId: 11, "قميص", new Money(20m, "JOD"), 1);

        order.Items.Single().UnitCost.Should().BeNull();
    }

    [Fact]
    public void تكلفة_بعملة_أخرى_لا_تُسجَّل_على_السطر()
    {
        // لقطةٌ بعملة غير عملة الطلب أسوأ من غياب لقطة: رقمٌ يُجمَع مع غيره ويُنتج هامشاً بلا معنى.
        var order = new Order(customerId: 1, "عمّان", "JOD");
        order.AddItem(productId: 1, variantId: 11, "قميص", new Money(20m, "JOD"), 1,
            unitCost: new Money(12m, "USD"));

        order.Items.Single().UnitCost.Should().BeNull();
    }

    private static Product SimpleProduct() => new("shirt", categoryId: 1,
        new Dictionary<string, CatalogText> { ["ar"] = new("قميص") }, new Money(20, "JOD"));

    private static void WithOption(Product product) =>
        product.SetOptions([new ProductOptionDefinition(null, new Dictionary<string, string> { ["ar"] = "المقاس" },
            [new(null, new Dictionary<string, string> { ["ar"] = "S" }), new(null, new Dictionary<string, string> { ["ar"] = "L" })],
            ExistingVariantsValue: 0)]);
}
