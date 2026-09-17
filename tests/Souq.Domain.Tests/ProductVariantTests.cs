using AwesomeAssertions;
using Souq.Domain.Common;
using Souq.Domain.Entities;
using Souq.Domain.Exceptions;
using Souq.Domain.ValueObjects;

namespace Souq.Domain.Tests;

// المتغيّرات قبل خياراتها (ProductVariants.md، V1): أيّ متغيّر يُشترى حين لا يُسمّى، ومتى يُشترى متغيّر مسمّى، والتعطيل
// بدل الحذف مع افتراضي نشط دائماً. متغيّر ثانٍ يُنشأ بـ AddVariant الداخلي — الطريق الوحيد إليه قبل نموذج الخيارات.
public class ProductVariantTests
{
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

    private static ProductVariant Second(Product product, decimal price = 25, int id = 12) =>
        WithId(product.AddVariant(new Money(price, "JOD"), sku: "SHIRT-L"), id);

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

        ((Action)(() => product.DeactivateVariant(11))).Should().Throw<InvalidProductDataException>();
        ((Action)(() => product.DeactivateVariant(99))).Should().Throw<InvalidProductDataException>();
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
        ((Action)(() => product.AddVariant(new Money(0, "JOD")))).Should().Throw<InvalidProductDataException>();
        ((Action)(() => product.AddVariant(new Money(10, "KWD")))).Should().Throw<InvalidProductDataException>();
        product.Price.Should().Be(new Money(20, "JOD"), "سعر المنتج يبقى اختصار متغيّره الافتراضي");
    }
}
