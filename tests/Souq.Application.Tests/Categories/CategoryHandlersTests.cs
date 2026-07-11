using FluentAssertions;
using NSubstitute;
using Souq.Application.Features.Categories.Commands;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;

namespace Souq.Application.Tests.Categories;

public class CreateCategoryHandlerTests
{
    private readonly ICategoryRepository _categories = Substitute.For<ICategoryRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private CreateCategoryHandler CreateHandler() => new(_categories, _uow);

    [Fact]
    public async Task slug_مستخدم_مسبقاً_يُرفض()
    {
        _categories.GetBySlugAsync("electronics", Arg.Any<CancellationToken>())
            .Returns(new Category("إلكترونيات", "electronics"));

        var result = await CreateHandler().Handle(
            new CreateCategoryCommand("إلكترونيات ٢", "electronics"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("SlugTaken");
    }

    [Fact]
    public async Task فئة_أب_غير_موجودة_تُرفض()
    {
        _categories.GetBySlugAsync("phones", Arg.Any<CancellationToken>()).Returns((Category?)null);
        _categories.GetByIdAsync(99, Arg.Any<CancellationToken>()).Returns((Category?)null);

        var result = await CreateHandler().Handle(
            new CreateCategoryCommand("هواتف", "phones", ParentId: 99), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("ParentNotFound");
    }

    [Fact]
    public async Task إنشاء_صالح_ينجح_ويحفظ()
    {
        _categories.GetBySlugAsync("electronics", Arg.Any<CancellationToken>()).Returns((Category?)null);

        var result = await CreateHandler().Handle(
            new CreateCategoryCommand("إلكترونيات", "electronics"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _categories.Received(1).AddAsync(Arg.Any<Category>(), Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}

public class UpdateCategoryHandlerTests
{
    private readonly ICategoryRepository _categories = Substitute.For<ICategoryRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private UpdateCategoryHandler CreateHandler() => new(_categories, _uow);

    [Fact]
    public async Task فئة_غير_موجودة_تُرجع_NotFound()
    {
        _categories.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns((Category?)null);

        var result = await CreateHandler().Handle(
            new UpdateCategoryCommand(1, "اسم", "slug"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("NotFound");
    }

    [Fact]
    public async Task slug_مستخدم_من_فئة_أخرى_يُرفض()
    {
        var category = new Category("إلكترونيات", "electronics");
        typeof(Category).GetProperty(nameof(Category.Id))!.SetValue(category, 1);
        var other = new Category("أزياء", "fashion");
        typeof(Category).GetProperty(nameof(Category.Id))!.SetValue(other, 2);

        _categories.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(category);
        _categories.GetBySlugAsync("fashion", Arg.Any<CancellationToken>()).Returns(other);

        var result = await CreateHandler().Handle(
            new UpdateCategoryCommand(1, "إلكترونيات", "fashion"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("SlugTaken");
    }

    [Fact]
    public async Task جعل_الفئة_أباً_لنفسها_يُرفض()
    {
        var category = new Category("إلكترونيات", "electronics");
        typeof(Category).GetProperty(nameof(Category.Id))!.SetValue(category, 1);
        _categories.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(category);
        _categories.GetBySlugAsync("electronics", Arg.Any<CancellationToken>()).Returns(category);

        var result = await CreateHandler().Handle(
            new UpdateCategoryCommand(1, "إلكترونيات", "electronics", ParentId: 1), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("InvalidParent");
    }

    [Fact]
    public async Task تحديث_صالح_ينجح()
    {
        var category = new Category("إلكترونيات", "electronics");
        typeof(Category).GetProperty(nameof(Category.Id))!.SetValue(category, 1);
        _categories.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(category);
        _categories.GetBySlugAsync("elec", Arg.Any<CancellationToken>()).Returns((Category?)null);

        var result = await CreateHandler().Handle(
            new UpdateCategoryCommand(1, "إلكترونيات جديد", "elec"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        category.Name.Should().Be("إلكترونيات جديد");
        category.Slug.Should().Be("elec");
    }
}

public class DeleteCategoryHandlerTests
{
    private readonly ICategoryRepository _categories = Substitute.For<ICategoryRepository>();
    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private DeleteCategoryHandler CreateHandler() => new(_categories, _products, _uow);

    [Fact]
    public async Task فئة_غير_موجودة_تُرجع_NotFound()
    {
        _categories.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns((Category?)null);

        var result = await CreateHandler().Handle(new DeleteCategoryCommand(1), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("NotFound");
    }

    [Fact]
    public async Task فئة_تحتوي_منتجات_لا_تُحذف()
    {
        _categories.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(new Category("إلكترونيات", "electronics"));
        _products.ExistsInCategoryAsync(1, Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateHandler().Handle(new DeleteCategoryCommand(1), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("CategoryInUse");
    }

    [Fact]
    public async Task فئة_لها_فئات_فرعية_لا_تُحذف()
    {
        _categories.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(new Category("إلكترونيات", "electronics"));
        _products.ExistsInCategoryAsync(1, Arg.Any<CancellationToken>()).Returns(false);
        _categories.HasChildrenAsync(1, Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateHandler().Handle(new DeleteCategoryCommand(1), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("CategoryHasChildren");
    }

    [Fact]
    public async Task حذف_صالح_ينجح()
    {
        _categories.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(new Category("إلكترونيات", "electronics"));
        _products.ExistsInCategoryAsync(1, Arg.Any<CancellationToken>()).Returns(false);
        _categories.HasChildrenAsync(1, Arg.Any<CancellationToken>()).Returns(false);

        var result = await CreateHandler().Handle(new DeleteCategoryCommand(1), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
