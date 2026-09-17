using AwesomeAssertions;
using NSubstitute;
using Souq.Application.Features.Products.Commands;
using Souq.Application.Features.Products.Contracts;
using Souq.Application.Features.Products.Queries;
using Souq.Application.Tests.TestDoubles;
using Souq.Domain.Entities;
using Souq.Domain.Exceptions;
using Souq.Domain.Interfaces;
using Souq.Domain.ValueObjects;
using static Souq.Application.Tests.TestDoubles.TestCatalog;

namespace Souq.Application.Tests.Products;

// حالات استخدام الخيارات والمتغيّرات (ADR-0040): الحلّ داخل منتج المتجر، لغة المتجر للأسماء، SKU فريد في المتجر، المخزون يُفتح
// في المعاملة نفسها، حارس تزامن الجذر قبل كل حفظ — والقواعد نفسها يطبّقها Product (ProductOptionTests).
public class ProductVariantHandlersTests
{
    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly IVariantStockInitializer _stock = Substitute.For<IVariantStockInitializer>();
    private readonly IUnitOfWork _uow = TestUnitOfWork.Create();

    private Product Shirt()
    {
        var shirt = Product("قميص", price: 20, id: 5);
        _products.GetByIdAsync(5, Arg.Any<CancellationToken>()).Returns(shirt);
        return shirt;
    }

    private Product ShirtWithSizes()
    {
        var shirt = Shirt();
        WithId(AddVariant(shirt, new Money(25, "JOD"), sku: "SHIRT-L"), 51);
        return shirt;
    }

    private static Dictionary<string, string> Ar(string name, string? en = null)
    {
        var names = new Dictionary<string, string> { ["ar"] = name };
        if (en is not null) names["en"] = en;
        return names;
    }

    private static SetProductOptionsCommand SizesCommand(int productId = 5) => new(productId,
        [new ProductOptionInput(null, Ar("المقاس", "Size"), [new(null, Ar("S")), new(null, Ar("M"))], ExistingVariantsValue: 0)]);

    // ── الخيارات ──

    [Fact]
    public async Task تعريف_الخيارات_يطبّقها_ويحرس_تزامن_الجذر_ثم_يحفظ()
    {
        var shirt = Shirt();

        var result = await new SetProductOptionsHandler(_products, TestTenant.Context(), _uow).Handle(SizesCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        shirt.Options.Single().Values.Select(v => v.NameIn("ar")).Should().Equal("S", "M");
        shirt.VariantLabel(shirt.DefaultVariant, "en").Should().Be("S");
        Received.InOrder(() =>
        {
            _products.GuardConcurrentEdit(shirt);
            _uow.SaveChangesAsync(Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task منتج_متجر_آخر_أو_غير_موجود_404_بلا_حفظ()
    {
        var result = await new SetProductOptionsHandler(_products, TestTenant.Context(), _uow).Handle(SizesCommand(productId: 77), CancellationToken.None);

        result.ErrorCode.Should().Be("NotFound");
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task أسماء_الخيار_وقيمه_بلغة_المتجر_الافتراضية_شرط()
    {
        Shirt();
        var englishValue = new SetProductOptionsCommand(5,
            [new ProductOptionInput(null, Ar("المقاس"), [new(null, new Dictionary<string, string> { ["en"] = "Small" })], 0)]);

        var result = await new SetProductOptionsHandler(_products, TestTenant.Context(), _uow).Handle(englishValue, CancellationToken.None);

        result.ErrorCode.Should().Be("DefaultTranslationRequired");
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task قاعدة_المجال_المكسورة_تصعد_برمزها_بلا_حفظ()
    {
        var shirt = ShirtWithSizes();
        var option = shirt.Options.Single();
        var dropLarge = new SetProductOptionsCommand(5, [new ProductOptionInput(option.Id, Ar("المقاس"),
            [new(option.Values.OrderBy(v => v.Position).First().Id, Ar("S"))])]);

        var act = () => new SetProductOptionsHandler(_products, TestTenant.Context(), _uow).Handle(dropLarge, CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidProductVariantException>()).Which.Code.Should().Be("OptionValueInUse");
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── إنشاء المتغيّرات ──

    [Fact]
    public async Task المتغيّرات_تُنشأ_بعملة_المنتج_ويُفتح_مخزون_كلٍّ_منها_في_المعاملة()
    {
        var shirt = ShirtWithSizes();
        var option = shirt.Options.Single();
        shirt.SetOptions([new ProductOptionDefinition(option.Id, Ar("المقاس"),
            [.. option.Values.OrderBy(v => v.Position).Select(v => new ProductOptionValueDefinition(v.Id, Ar(v.NameIn("ar")))),
             new(null, Ar("XL")), new(null, Ar("XXL"))])]);
        foreach (var (value, i) in option.Values.Where(v => v.Id == 0).Select((v, i) => (v, i))) WithId(value, 700 + i);

        var result = await new CreateProductVariantsHandler(_products, _stock, _uow).Handle(new CreateProductVariantsCommand(5,
        [
            new NewProductVariantInput([700], 30, 35, "shirt-xl", InitialStock: 4, LowStockThreshold: 2),
            new NewProductVariantInput([701], 31, IsActive: false),
        ]), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var created = shirt.Variants.Where(v => v.Price.Amount >= 30).ToList();
        created.Select(v => (v.Price, v.Sku, v.IsActive)).Should().Equal(
            (new Money(30, "JOD"), "SHIRT-XL", true), (new Money(31, "JOD"), (string?)null, false));
        await _uow.Received(1).InTransactionAsync(Arg.Any<Func<Task>>(), Arg.Any<CancellationToken>());
        await _stock.Received(1).InitializeAsync(Arg.Is(5), Arg.Any<int>(), Arg.Is(4), Arg.Is(2), Arg.Any<CancellationToken>());
        await _stock.Received(1).InitializeAsync(
            Arg.Is(5), Arg.Any<int>(), Arg.Is(0), Arg.Is(InventoryItem.DefaultLowStockThreshold), Arg.Any<CancellationToken>());
        _products.Received(1).GuardConcurrentEdit(shirt);
    }

    [Fact]
    public async Task SKU_مستخدم_في_منتج_آخر_بالمتجر_يُرفض_قبل_الحفظ_وفتح_المخزون()
    {
        var shirt = ShirtWithSizes();
        var option = shirt.Options.Single();
        shirt.SetOptions([new ProductOptionDefinition(option.Id, Ar("المقاس"),
            [.. option.Values.OrderBy(v => v.Position).Select(v => new ProductOptionValueDefinition(v.Id, Ar(v.NameIn("ar")))), new(null, Ar("XL"))])]);
        WithId(option.Values.Single(v => v.Id == 0), 700);
        _products.SkuExistsAsync("TAKEN", 5, Arg.Any<CancellationToken>()).Returns(true);

        var result = await new CreateProductVariantsHandler(_products, _stock, _uow)
            .Handle(new CreateProductVariantsCommand(5, [new NewProductVariantInput([700], 30, Sku: "taken")]), CancellationToken.None);

        result.ErrorCode.Should().Be("SkuTaken");
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _stock.DidNotReceive().InitializeAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task قيمة_لا_تخصّ_المنتج_تُرفض_من_المجال_بلا_حفظ()
    {
        ShirtWithSizes();

        var act = () => new CreateProductVariantsHandler(_products, _stock, _uow)
            .Handle(new CreateProductVariantsCommand(5, [new NewProductVariantInput([123456], 30)]), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidProductVariantException>()).Which.Code.Should().Be("OptionValueNotFound");
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── تعديل، تفعيل، افتراضي ──

    [Fact]
    public async Task متغيّر_منتج_آخر_404_في_كل_أوامر_المتغيّر()
    {
        ShirtWithSizes();
        var other = Product("شاحن", id: 6);
        WithId(AddVariant(other, new Money(9, "JOD")), 61);
        _products.GetByIdAsync(6, Arg.Any<CancellationToken>()).Returns(other);

        (await new UpdateProductVariantHandler(_products, _uow).Handle(new(5, 61, 10), CancellationToken.None)).ErrorCode.Should().Be("NotFound");
        (await new SetProductVariantStatusHandler(_products, _uow).Handle(new(5, 61, false), CancellationToken.None)).ErrorCode.Should().Be("NotFound");
        (await new SetDefaultProductVariantHandler(_products, _uow).Handle(new(5, 61), CancellationToken.None)).ErrorCode.Should().Be("NotFound");
        (await new UpdateProductVariantHandler(_products, _uow).Handle(new(99, 51, 10), CancellationToken.None)).ErrorCode.Should().Be("NotFound");

        other.FindVariant(61)!.IsActive.Should().BeTrue();
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task تعديل_متغيّر_بعملة_المنتج_وSKU_فريد_في_المتجر()
    {
        var shirt = ShirtWithSizes();
        _products.SkuExistsAsync("TAKEN", 5, Arg.Any<CancellationToken>()).Returns(true);

        var ok = await new UpdateProductVariantHandler(_products, _uow).Handle(new(5, 51, 27.5m, 30, "shirt-large"), CancellationToken.None);
        var taken = await new UpdateProductVariantHandler(_products, _uow).Handle(new(5, 51, 27.5m, null, "taken"), CancellationToken.None);

        ok.IsSuccess.Should().BeTrue();
        taken.ErrorCode.Should().Be("SkuTaken");
        shirt.FindVariant(51)!.Price.Should().Be(new Money(27.5m, "JOD"));
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task تعطيل_وتفعيل_والافتراضي_لا_يُعطَّل()
    {
        var shirt = ShirtWithSizes();
        var handler = new SetProductVariantStatusHandler(_products, _uow);

        (await handler.Handle(new(5, 51, false), CancellationToken.None)).IsSuccess.Should().BeTrue();
        shirt.FindVariant(51)!.IsActive.Should().BeFalse();
        (await handler.Handle(new(5, 51, true), CancellationToken.None)).IsSuccess.Should().BeTrue();

        var act = () => handler.Handle(new(5, 5, false), CancellationToken.None);
        (await act.Should().ThrowAsync<InvalidProductVariantException>()).Which.Code.Should().Be("DefaultVariantCannotBeDeactivated");
        shirt.DefaultVariant.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task الافتراضي_الجديد_نشط_ويصير_سعر_المنتج()
    {
        var shirt = ShirtWithSizes();

        var result = await new SetDefaultProductVariantHandler(_products, _uow).Handle(new(5, 51), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        shirt.DefaultVariant.Id.Should().Be(51);
        shirt.Price.Should().Be(new Money(25, "JOD"));
        _products.Received(1).GuardConcurrentEdit(shirt);
    }

    [Fact]
    public async Task تعديل_المنتج_من_نموذجه_لا_يغيّر_سعر_منتج_بخيارات()
    {
        var shirt = ShirtWithSizes();
        var categories = Substitute.For<ICategoryRepository>();
        categories.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(Category("فئة", "cat"));
        var handler = new UpdateProductHandler(_products, categories, TestTenant.Context(), _uow);

        var unchanged = await handler.Handle(new UpdateProductCommand(5, 1, Input("قميص قطني"), 20, shirt.Slug), CancellationToken.None);
        var act = () => handler.Handle(new UpdateProductCommand(5, 1, Input("قميص قطني"), 22, shirt.Slug), CancellationToken.None);

        unchanged.IsSuccess.Should().BeTrue("عميل يعيد السعر الذي قرأه يعدّل الاسم كما كان");
        shirt.NameIn("ar").Should().Be("قميص قطني");
        (await act.Should().ThrowAsync<InvalidProductVariantException>()).Which.Code.Should().Be("ProductHasVariants");
        shirt.Price.Should().Be(new Money(20, "JOD"));
    }

    // ── التحقّق الشكلي ──

    [Fact]
    public void المدقّقات_تفرض_الحدود_المنشورة_قبل_المعالج()
    {
        var names = Ar("المقاس");
        var value = new ProductOptionValueInput(null, Ar("S"));
        new SetProductOptionsValidator().Validate(new SetProductOptionsCommand(5,
            Enumerable.Range(0, 4).Select(_ => new ProductOptionInput(null, names, [value], 0)).ToList())).IsValid.Should().BeFalse();
        new SetProductOptionsValidator().Validate(new SetProductOptionsCommand(5,
            [new ProductOptionInput(null, names, Enumerable.Repeat(value, 21).ToList(), 0)])).IsValid.Should().BeFalse();
        new SetProductOptionsValidator().Validate(new SetProductOptionsCommand(5,
            [new ProductOptionInput(null, Ar(new string('م', 51)), [value], 0)])).IsValid.Should().BeFalse();
        new SetProductOptionsValidator().Validate(new SetProductOptionsCommand(5, [new ProductOptionInput(null, names, [], 0)])).IsValid.Should().BeFalse();
        new SetProductOptionsValidator().Validate(new SetProductOptionsCommand(0, [])).IsValid.Should().BeFalse();
        new SetProductOptionsValidator().Validate(SizesCommand()).IsValid.Should().BeTrue();

        var variant = new NewProductVariantInput([1, 2], 10);
        new CreateProductVariantsValidator().Validate(new CreateProductVariantsCommand(5, Enumerable.Repeat(variant, 101).ToList())).IsValid.Should().BeFalse();
        new CreateProductVariantsValidator().Validate(new CreateProductVariantsCommand(5, [])).IsValid.Should().BeFalse();
        new CreateProductVariantsValidator().Validate(new CreateProductVariantsCommand(5, [variant with { Price = 0 }])).IsValid.Should().BeFalse();
        new CreateProductVariantsValidator().Validate(new CreateProductVariantsCommand(5, [variant with { CompareAtPrice = 10 }])).IsValid.Should().BeFalse();
        new CreateProductVariantsValidator().Validate(new CreateProductVariantsCommand(5, [variant with { InitialStock = -1 }])).IsValid.Should().BeFalse();
        new CreateProductVariantsValidator().Validate(new CreateProductVariantsCommand(5, [variant with { OptionValueIds = [1, 2, 3, 4] }])).IsValid.Should().BeFalse();
        new CreateProductVariantsValidator().Validate(new CreateProductVariantsCommand(5, [variant])).IsValid.Should().BeTrue();

        new UpdateProductVariantValidator().Validate(new UpdateProductVariantCommand(5, 0, 10)).IsValid.Should().BeFalse();
        new UpdateProductVariantValidator().Validate(new UpdateProductVariantCommand(5, 51, 10, 9)).IsValid.Should().BeFalse();
        new UpdateProductVariantValidator().Validate(new UpdateProductVariantCommand(5, 51, 10, null, new string('A', 65))).IsValid.Should().BeFalse();
        new SetProductVariantStatusValidator().Validate(new SetProductVariantStatusCommand(5, 0, true)).IsValid.Should().BeFalse();
        new SetDefaultProductVariantValidator().Validate(new SetDefaultProductVariantCommand(0, 51)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void الأوامر_كلها_مدقَّقة_بهدفها()
    {
        new SetProductOptionsCommand(5, []).ToAuditRecord().Should().Match<Souq.Application.Common.Auditing.AuditRecord>(
            a => a.Action == "catalog.product.options-set" && a.TargetType == "Product" && a.TargetId == "5");
        new CreateProductVariantsCommand(5, []).ToAuditRecord().Action.Should().Be("catalog.product.variants-created");
        new UpdateProductVariantCommand(5, 51, 10).ToAuditRecord().Should().Match<Souq.Application.Common.Auditing.AuditRecord>(
            a => a.TargetType == "ProductVariant" && a.TargetId == "51");
        new SetProductVariantStatusCommand(5, 51, false).ToAuditRecord().Action.Should().Be("catalog.product.variant-deactivated");
        new SetDefaultProductVariantCommand(5, 51).ToAuditRecord().Action.Should().Be("catalog.product.default-variant-set");
    }
}
