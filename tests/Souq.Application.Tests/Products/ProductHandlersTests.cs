using AwesomeAssertions;
using NSubstitute;
using Souq.Application.Features.Products.Commands;
using Souq.Application.Features.Products.Queries;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Interfaces;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Tests.Products;

public class CreateProductHandlerTests
{
    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly IStockMovementRepository _stockMovements = Substitute.For<IStockMovementRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    [Fact]
    public async Task ينشئ_المنتج_ويسجّل_مخزونه_الابتدائي_حركة_توريد()
    {
        var handler = new CreateProductHandler(_products, _stockMovements, _uow);
        var cmd = new CreateProductCommand("سماعات", "وصف", 59.9m, 10, "headphones", CategoryId: 1);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _products.Received(1).AddAsync(Arg.Any<Product>(), Arg.Any<CancellationToken>());
        // المخزون الابتدائي (10) يُسجَّل حركة Purchase موجبة.
        await _stockMovements.Received(1).AddAsync(
            Arg.Is<StockMovement>(m => m.Type == StockMovementType.Purchase && m.QuantityChange == 10),
            Arg.Any<CancellationToken>());
        // حفظ أول للمنتج (يولّد المعرّف)، ثم ثانٍ للحركة المرتبطة بمعرّفه.
        await _uow.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task منتج_بمخزون_صفر_يُحفظ_مرة_واحدة_بلا_حركة()
    {
        var handler = new CreateProductHandler(_products, _stockMovements, _uow);
        var cmd = new CreateProductCommand("سماعات", "وصف", 59.9m, 0, "headphones", CategoryId: 1);

        var result = await handler.Handle(cmd, CancellationToken.None);

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

    private UpdateProductHandler CreateHandler() => new(_products, _categories, _stockMovements, _uow);

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
        var product = NewProduct(); // مخزون 10
        _products.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(product);
        _categories.GetByIdAsync(2, Arg.Any<CancellationToken>()).Returns(new Category("أزياء", "fashion"));

        // المدير رأى 10 وعدّله إلى 20 — المخزون لم يتغيّر منذ فتح النموذج ⇒ مقبول.
        var result = await CreateHandler().Handle(
            new UpdateProductCommand(1, "اسم جديد", "وصف جديد", 75, 20, "new.jpg", 2, ExpectedStockQuantity: 10),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        product.Name.Should().Be("اسم جديد");
        product.Price.Amount.Should().Be(75);
        product.Price.Currency.Should().Be("JOD"); // العملة الأصلية بقيت كما هي
        product.StockQuantity.Should().Be(20);
        product.CategoryId.Should().Be(2);

        // تغيّر المخزون (10 ⇒ 20) يُسجَّل حركة تصحيح بفارق موجب (+10).
        await _stockMovements.Received(1).AddAsync(
            Arg.Is<StockMovement>(m => m.Type == StockMovementType.Adjustment && m.QuantityChange == 10),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task تعديل_الاسم_وحده_لا_يمسّ_المخزون_ولا_يسجّل_حركة()
    {
        var product = NewProduct(); // مخزون 10
        _products.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(product);
        _categories.GetByIdAsync(2, Arg.Any<CancellationToken>()).Returns(new Category("أزياء", "fashion"));

        // StockQuantity = null: المدير لم يلمس حقل المخزون — لا نكتب فوق مبيعات متزامنة.
        var result = await CreateHandler().Handle(
            new UpdateProductCommand(1, "اسم", "وصف", 75, null, "img.jpg", 2), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        product.StockQuantity.Should().Be(10);
        await _stockMovements.DidNotReceive().AddAsync(Arg.Any<StockMovement>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task مخزون_تغيّر_منذ_فتح_النموذج_يُرفض_بتعارض_ولا_يُعدَّل_شيء()
    {
        // Phase 0 C4: فتح المدير النموذج والمخزون 10، بِيعت 3 (صار 7)، ثم حفظ 20 —
        // كان يُمحى البيع ويُسجَّل "تصحيح" وهمي. الآن: تعارض واضح ولا تغيير جزئي.
        var product = NewProduct();
        product.DecreaseStock(3); // 10 ⇒ 7 بعد فتح النموذج
        _products.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(product);
        _categories.GetByIdAsync(2, Arg.Any<CancellationToken>()).Returns(new Category("أزياء", "fashion"));

        var result = await CreateHandler().Handle(
            new UpdateProductCommand(1, "اسم جديد", "وصف", 75, 20, "img.jpg", 2, ExpectedStockQuantity: 10),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        // رمز خاص يميّزه عن تعارض rowversion (ConcurrencyConflict): الواجهة تعرف أن المخزون تحديداً تغيّر.
        result.ErrorCode.Should().Be("StockChanged");
        result.Error!.Kind.Should().Be(Souq.Application.Common.Models.ErrorKind.Conflict);
        product.StockQuantity.Should().Be(7);
        product.Name.Should().Be("سماعات"); // لا تعديل جزئي للتفاصيل مع رفض المخزون
        await _stockMovements.DidNotReceive().AddAsync(Arg.Any<StockMovement>(), Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
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
    private readonly ICatalogQueries _catalog = Substitute.For<ICatalogQueries>();

    private GetProductByIdHandler CreateHandler() => new(_catalog);

    [Fact]
    public async Task منتج_غير_موجود_أو_معطّل_يُرجع_NotFound()
    {
        // FindActiveProductAsync تُرجع null لكل من المنتج غير الموجود والمعطّل (تصفية IsActive
        // في SQL — اختبار تكامل) فلا تسريب لبيانات معطّلة.
        _catalog.FindActiveProductAsync(1, Arg.Any<CancellationToken>()).Returns((ProductDto?)null);

        var result = await CreateHandler().Handle(new GetProductByIdQuery(1), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("NotFound");
    }

    [Fact]
    public async Task منتج_نشط_يُعاد_كما_أسقطه_منفذ_القراءة()
    {
        var dto = new ProductDto(1, "سماعات", "Headphones", "وصف", 50, "JOD", 10, "img", null, 1, "إلكترونيات");
        _catalog.FindActiveProductAsync(1, Arg.Any<CancellationToken>()).Returns(dto);

        var result = await CreateHandler().Handle(new GetProductByIdQuery(1), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(dto);
    }
}
