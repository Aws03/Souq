using FluentAssertions;
using NSubstitute;
using Souq.Application.Features.Products.Commands;
using Souq.Application.Features.Products.Queries;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Tests.Products;

public class CreateProductHandlerTests
{
    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    [Fact]
    public async Task ينشئ_المنتج_ويحفظه_مرة_واحدة()
    {
        var handler = new CreateProductHandler(_products, _uow);
        var cmd = new CreateProductCommand("سماعات", "وصف", 59.9m, 10, "headphones", CategoryId: 1);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _products.Received(1).AddAsync(Arg.Any<Product>(), Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}

public class UpdateProductHandlerTests
{
    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly ICategoryRepository _categories = Substitute.For<ICategoryRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private UpdateProductHandler CreateHandler() => new(_products, _categories, _uow);

    private static Product NewProduct() =>
        new("سماعات", "وصف", new Money(50), 10, "headphones", categoryId: 1);

    [Fact]
    public async Task منتج_غير_موجود_يُرجع_NotFound()
    {
        _products.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns((Product?)null);

        var result = await CreateHandler().Handle(
            new UpdateProductCommand(1, "س", "و", 10, 1, "img", 2), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("NotFound");
    }

    [Fact]
    public async Task فئة_غير_موجودة_يُرجع_CategoryNotFound()
    {
        _products.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(NewProduct());
        _categories.GetByIdAsync(2, Arg.Any<CancellationToken>()).Returns((Category?)null);

        var result = await CreateHandler().Handle(
            new UpdateProductCommand(1, "س", "و", 10, 1, "img", 2), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("CategoryNotFound");
    }

    [Fact]
    public async Task تحديث_صالح_يعدّل_الحقول_ويحفظ_العملة_الأصلية()
    {
        var product = NewProduct();
        _products.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(product);
        _categories.GetByIdAsync(2, Arg.Any<CancellationToken>()).Returns(new Category("أزياء", "fashion"));

        var result = await CreateHandler().Handle(
            new UpdateProductCommand(1, "اسم جديد", "وصف جديد", 75, 20, "new.jpg", 2), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        product.Name.Should().Be("اسم جديد");
        product.Price.Amount.Should().Be(75);
        product.Price.Currency.Should().Be("JOD"); // العملة الأصلية بقيت كما هي
        product.StockQuantity.Should().Be(20);
        product.CategoryId.Should().Be(2);
    }
}

public class DeleteProductHandlerTests
{
    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private DeleteProductHandler CreateHandler() => new(_products, _uow);

    [Fact]
    public async Task منتج_غير_موجود_يُرجع_NotFound()
    {
        _products.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns((Product?)null);

        var result = await CreateHandler().Handle(new DeleteProductCommand(1), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("NotFound");
    }

    [Fact]
    public async Task حذف_صالح_يعطّل_المنتج_منطقياً()
    {
        var product = new Product("سماعات", "وصف", new Money(50), 10, "headphones", categoryId: 1);
        _products.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(product);

        var result = await CreateHandler().Handle(new DeleteProductCommand(1), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        product.IsActive.Should().BeFalse();
    }
}

public class GetProductByIdHandlerTests
{
    private readonly IProductRepository _products = Substitute.For<IProductRepository>();

    private GetProductByIdHandler CreateHandler() => new(_products);

    [Fact]
    public async Task منتج_غير_موجود_أو_معطّل_يُرجع_NotFound()
    {
        // GetActiveByIdAsync هي ما يستخدمه هذا المعالج تحديداً (مرحلة 4، بند AUDIT ١١) —
        // تُرجع null لكل من المنتج غير الموجود والمعطّل، فلا تسريب لبيانات معطّلة.
        _products.GetActiveByIdAsync(1, Arg.Any<CancellationToken>()).Returns((Product?)null);

        var result = await CreateHandler().Handle(new GetProductByIdQuery(1), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("NotFound");
    }

    [Fact]
    public async Task منتج_نشط_يُعاد_مع_اسم_فئته()
    {
        var category = new Category("إلكترونيات", "electronics");
        var product = new Product("سماعات", "وصف", new Money(50), 10, "headphones", categoryId: 1);
        typeof(Product).GetProperty(nameof(Product.Category))!.SetValue(product, category);
        _products.GetActiveByIdAsync(1, Arg.Any<CancellationToken>()).Returns(product);

        var result = await CreateHandler().Handle(new GetProductByIdQuery(1), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.CategoryName.Should().Be("إلكترونيات");
    }
}
