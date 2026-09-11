using AwesomeAssertions;
using NSubstitute;
using Souq.Application.Features.Products.Commands;
using Souq.Application.Features.Products.Queries;
using Souq.Application.Tests.TestDoubles;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Interfaces;
using static Souq.Application.Tests.TestDoubles.TestCatalog;

namespace Souq.Application.Tests.Products;

public class CreateProductHandlerTests
{
    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly IStockMovementRepository _stockMovements = Substitute.For<IStockMovementRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ICategoryRepository _categories = Substitute.For<ICategoryRepository>();

    public CreateProductHandlerTests() =>
        _categories.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(Category("فئة", "cat"));

    private CreateProductHandler Handler(string currency = "JOD") =>
        new(_products, _categories, _stockMovements, TestTenant.Context(currency), _uow);

    private static CreateProductCommand Command(int categoryId = 1, int stock = 10, string? slug = null, string? sku = null) =>
        new(categoryId, Input("سماعات", "Wireless Headphones"), 59.9m, stock, Slug: slug, Sku: sku);

    [Fact]
    public async Task فئة_ليست_في_المتجر_تُرفض_قبل_أي_حفظ()
    {
        var result = await Handler().Handle(Command(categoryId: 99), CancellationToken.None);

        result.ErrorCode.Should().Be("CategoryNotFound");
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task اسم_بلغة_المتجر_الافتراضية_شرط()
    {
        var englishOnly = new CreateProductCommand(1, new Dictionary<string, CatalogTextInput> { ["en"] = new("Headphones") }, 10m, 1);

        var result = await Handler().Handle(englishOnly, CancellationToken.None);

        result.ErrorCode.Should().Be("DefaultTranslationRequired");
    }

    [Fact]
    public async Task السعر_بعملة_المتجر_والمعرّف_من_الاسم_اللاتيني()
    {
        Product? added = null;
        _products.When(p => p.AddAsync(Arg.Any<Product>(), Arg.Any<CancellationToken>()))
                 .Do(call => added = call.Arg<Product>());

        await Handler("USD").Handle(Command(stock: 0), CancellationToken.None);

        added!.Price.Currency.Should().Be("USD");
        added.Slug.Should().Be("wireless-headphones");
        added.NameIn("en").Should().Be("Wireless Headphones");
    }

    [Fact]
    public async Task معرّف_مقترَح_مكرّر_يأخذ_لاحقة_ومعرّف_المدير_المكرّر_يُرفض()
    {
        _products.SlugExistsAsync("wireless-headphones", null, Arg.Any<CancellationToken>()).Returns(true);
        Product? added = null;
        _products.When(p => p.AddAsync(Arg.Any<Product>(), Arg.Any<CancellationToken>()))
                 .Do(call => added = call.Arg<Product>());

        (await Handler().Handle(Command(), CancellationToken.None)).IsSuccess.Should().BeTrue();
        added!.Slug.Should().Be("wireless-headphones-2");

        var explicitSlug = await Handler().Handle(Command(slug: "wireless-headphones"), CancellationToken.None);
        explicitSlug.ErrorCode.Should().Be("ProductSlugTaken");
    }

    [Fact]
    public async Task SKU_مستخدم_في_المتجر_يُرفض()
    {
        _products.SkuExistsAsync("HP-1", null, Arg.Any<CancellationToken>()).Returns(true);

        var result = await Handler().Handle(Command(sku: "hp-1"), CancellationToken.None);

        result.ErrorCode.Should().Be("SkuTaken");
        await _products.DidNotReceive().AddAsync(Arg.Any<Product>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ينشئ_المنتج_ويسجّل_مخزونه_الابتدائي_حركة_توريد()
    {
        var result = await Handler().Handle(Command(stock: 10), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _stockMovements.Received(1).AddAsync(
            Arg.Is<StockMovement>(m => m.Type == StockMovementType.Purchase && m.QuantityChange == 10),
            Arg.Any<CancellationToken>());
        // حفظ أول للمنتج (يولّد المعرّف)، ثم ثانٍ للحركة المرتبطة بمعرّفه.
        await _uow.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task منتج_بمخزون_صفر_يُحفظ_مرة_واحدة_بلا_حركة()
    {
        var result = await Handler().Handle(Command(stock: 0), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _stockMovements.DidNotReceive().AddAsync(Arg.Any<StockMovement>(), Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}

public class UpdateProductHandlerTests
{
    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly ICategoryRepository _categories = Substitute.For<ICategoryRepository>();
    private readonly IStockMovementRepository _stockMovements = Substitute.For<IStockMovementRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private UpdateProductHandler CreateHandler() => new(_products, _categories, _stockMovements, TestTenant.Context(), _uow);

    private static UpdateProductCommand Command(
        string name = "اسم جديد", decimal price = 75, int? stock = null, int? expected = null, int categoryId = 2) =>
        new(1, categoryId, Input(name), price, "new-slug", StockQuantity: stock, ExpectedStockQuantity: expected);

    [Fact]
    public async Task منتج_غير_موجود_يُرجع_NotFound()
    {
        _products.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns((Product?)null);

        var result = await CreateHandler().Handle(Command(), CancellationToken.None);

        result.ErrorCode.Should().Be("NotFound");
    }

    [Fact]
    public async Task فئة_غير_موجودة_يُرجع_CategoryNotFound()
    {
        _products.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(Product());
        _categories.GetByIdAsync(2, Arg.Any<CancellationToken>()).Returns((Category?)null);

        var result = await CreateHandler().Handle(Command(), CancellationToken.None);

        result.ErrorCode.Should().Be("CategoryNotFound");
    }

    [Fact]
    public async Task تحديث_صالح_يعدّل_الحقول_ويحفظ_العملة_الأصلية()
    {
        var product = Product(stock: 10);
        _products.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(product);
        _categories.GetByIdAsync(2, Arg.Any<CancellationToken>()).Returns(Category("أزياء", "fashion"));

        // المدير رأى 10 وعدّله إلى 20 — المخزون لم يتغيّر منذ فتح النموذج ⇒ مقبول.
        var result = await CreateHandler().Handle(Command(stock: 20, expected: 10), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        product.NameIn("ar").Should().Be("اسم جديد");
        product.Slug.Should().Be("new-slug");
        product.Price.Amount.Should().Be(75);
        product.Price.Currency.Should().Be("JOD");
        product.StockQuantity.Should().Be(20);
        product.CategoryId.Should().Be(2);
        await _stockMovements.Received(1).AddAsync(
            Arg.Is<StockMovement>(m => m.Type == StockMovementType.Adjustment && m.QuantityChange == 10),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task تعديل_الاسم_وحده_لا_يمسّ_المخزون_ولا_يسجّل_حركة()
    {
        var product = Product(stock: 10);
        _products.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(product);
        _categories.GetByIdAsync(2, Arg.Any<CancellationToken>()).Returns(Category("أزياء", "fashion"));

        var result = await CreateHandler().Handle(Command(stock: null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        product.StockQuantity.Should().Be(10);
        await _stockMovements.DidNotReceive().AddAsync(Arg.Any<StockMovement>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task مخزون_تغيّر_منذ_فتح_النموذج_يُرفض_بتعارض_ولا_يُعدَّل_شيء()
    {
        // Phase 0 C4: فتح المدير النموذج والمخزون 10، بِيعت 3 (صار 7)، ثم حفظ 20 — تعارض واضح ولا تغيير جزئي.
        var product = Product(stock: 10);
        product.DecreaseStock(3);
        _products.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(product);
        _categories.GetByIdAsync(2, Arg.Any<CancellationToken>()).Returns(Category("أزياء", "fashion"));

        var result = await CreateHandler().Handle(Command(stock: 20, expected: 10), CancellationToken.None);

        result.ErrorCode.Should().Be("StockChanged");
        result.Error!.Kind.Should().Be(Souq.Application.Common.Models.ErrorKind.Conflict);
        product.StockQuantity.Should().Be(7);
        product.NameIn("ar").Should().Be("سماعات");
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}

public class ProductLifecycleHandlerTests
{
    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    [Fact]
    public async Task حذف_المنتج_أرشفة_لا_حذف()
    {
        var product = Product();
        _products.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(product);

        var result = await new DeleteProductHandler(_products, _uow).Handle(new DeleteProductCommand(1), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        product.Status.Should().Be(ProductStatus.Archived);
        _products.DidNotReceive().Remove(Arg.Any<Product>());
    }

    [Fact]
    public async Task المؤرشف_يُستعاد_بتغيير_حالته()
    {
        var product = Product();
        product.Archive();
        _products.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(product);

        var result = await new ChangeProductStatusHandler(_products, _uow)
            .Handle(new ChangeProductStatusCommand(1, ProductStatus.Active), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        product.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task صورة_ليست_لهذا_المنتج_غير_موجودة()
    {
        _products.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(Product());

        var result = await new RemoveProductImageHandler(_products, _uow)
            .Handle(new RemoveProductImageCommand(1, 42), CancellationToken.None);

        result.ErrorCode.Should().Be("NotFound");
    }
}

public class GetProductByIdHandlerTests
{
    private readonly ICatalogQueries _catalog = Substitute.For<ICatalogQueries>();

    private GetProductByIdHandler CreateHandler() => new(_catalog, TestTenant.Context());

    [Fact]
    public async Task منتج_غير_معروض_يُرجع_NotFound_ولغة_المتجر_تُمرَّر()
    {
        _catalog.FindActiveProductAsync(1, "ar", Arg.Any<CancellationToken>()).Returns((ProductDto?)null);

        var result = await CreateHandler().Handle(new GetProductByIdQuery(1), CancellationToken.None);

        result.ErrorCode.Should().Be("NotFound");
        await _catalog.Received(1).FindActiveProductAsync(1, "ar", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task منتج_معروض_يُعاد_كما_أسقطه_منفذ_القراءة()
    {
        var dto = Dto(1);
        _catalog.FindActiveProductAsync(1, "ar", Arg.Any<CancellationToken>()).Returns(dto);

        var result = await CreateHandler().Handle(new GetProductByIdQuery(1), CancellationToken.None);

        result.Value.Should().Be(dto);
    }

    [Fact]
    public async Task البحث_بالمعرّف_النصّي_مطبَّع()
    {
        _catalog.FindActiveProductBySlugAsync("wireless-headphones", "ar", Arg.Any<CancellationToken>()).Returns(Dto(1));

        var result = await CreateHandler().Handle(new GetProductBySlugQuery(" Wireless-Headphones "), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }
}
