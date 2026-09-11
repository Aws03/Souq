using AwesomeAssertions;
using NSubstitute;
using Souq.Application.Features.Categories.Commands;
using Souq.Application.Tests.TestDoubles;
using Souq.Domain.Entities;
using Souq.Domain.Exceptions;
using Souq.Domain.Interfaces;
using static Souq.Application.Tests.TestDoubles.TestCatalog;

namespace Souq.Application.Tests.Categories;

public class CreateCategoryHandlerTests
{
    private readonly ICategoryRepository _categories = Substitute.For<ICategoryRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private CreateCategoryHandler CreateHandler() => new(_categories, TestTenant.Context(), _uow);

    [Fact]
    public async Task slug_مستخدم_مسبقاً_يُرفض()
    {
        _categories.GetBySlugAsync("electronics", Arg.Any<CancellationToken>()).Returns(Category("إلكترونيات", "electronics"));

        var result = await CreateHandler().Handle(new CreateCategoryCommand("electronics", Input("إلكترونيات ٢")), CancellationToken.None);

        result.ErrorCode.Should().Be("SlugTaken");
    }

    [Fact]
    public async Task فئة_أب_غير_موجودة_تُرفض()
    {
        _categories.ListLinksAsync(Arg.Any<CancellationToken>()).Returns([new CategoryLink(1, null)]);

        var result = await CreateHandler().Handle(
            new CreateCategoryCommand("phones", Input("هواتف"), ParentId: 99), CancellationToken.None);

        result.ErrorCode.Should().Be("ParentNotFound");
    }

    [Fact]
    public async Task اسم_بلغة_المتجر_الافتراضية_شرط()
    {
        var result = await CreateHandler().Handle(
            new CreateCategoryCommand("phones", new Dictionary<string, Souq.Application.Features.Products.Queries.CatalogTextInput>
            {
                ["en"] = new("Phones"),
            }), CancellationToken.None);

        result.ErrorCode.Should().Be("DefaultTranslationRequired");
    }

    [Fact]
    public async Task إنشاء_تحت_أب_عميق_يُرفض_بحدّ_العمق()
    {
        // سلسلة 5 ⇒ 4 ⇒ 3 ⇒ 2 ⇒ 1: الأب على العمق 5، والابن سيكون السادس.
        _categories.ListLinksAsync(Arg.Any<CancellationToken>()).Returns([
            new CategoryLink(1, null), new CategoryLink(2, 1), new CategoryLink(3, 2), new CategoryLink(4, 3), new CategoryLink(5, 4),
        ]);

        var act = () => CreateHandler().Handle(new CreateCategoryCommand("deep", Input("عميقة"), ParentId: 5), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidCategoryParentException>();
    }

    [Fact]
    public async Task إنشاء_صالح_ينجح_ويحفظ()
    {
        var result = await CreateHandler().Handle(new CreateCategoryCommand("electronics", Input("إلكترونيات")), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _categories.Received(1).AddAsync(Arg.Is<Category>(c => c.Slug == "electronics" && c.IsActive), Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}

public class UpdateCategoryHandlerTests
{
    private readonly ICategoryRepository _categories = Substitute.For<ICategoryRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private UpdateCategoryHandler CreateHandler() => new(_categories, TestTenant.Context(), _uow);

    [Fact]
    public async Task فئة_غير_موجودة_تُرجع_NotFound()
    {
        _categories.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns((Category?)null);

        var result = await CreateHandler().Handle(new UpdateCategoryCommand(1, "slug", Input("اسم")), CancellationToken.None);

        result.ErrorCode.Should().Be("NotFound");
    }

    [Fact]
    public async Task slug_مستخدم_من_فئة_أخرى_يُرفض()
    {
        _categories.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(Category("إلكترونيات", "electronics", id: 1));
        _categories.GetBySlugAsync("fashion", Arg.Any<CancellationToken>()).Returns(Category("أزياء", "fashion", id: 2));

        var result = await CreateHandler().Handle(new UpdateCategoryCommand(1, "fashion", Input("إلكترونيات")), CancellationToken.None);

        result.ErrorCode.Should().Be("SlugTaken");
    }

    [Fact]
    public async Task نقل_الفئة_تحت_فرعها_يرفضه_الكيان()
    {
        // 1 ⇒ 2 ⇒ 3: نقل 1 تحت 3 يصنع حلقة.
        _categories.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(Category("جذر", "root", id: 1));
        _categories.ListLinksAsync(Arg.Any<CancellationToken>()).Returns([
            new CategoryLink(1, null), new CategoryLink(2, 1), new CategoryLink(3, 2),
        ]);

        var act = () => CreateHandler().Handle(new UpdateCategoryCommand(1, "root", Input("جذر"), ParentId: 3), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidCategoryParentException>();
    }

    [Fact]
    public async Task تحديث_صالح_ينجح()
    {
        var category = Category("إلكترونيات", "electronics", id: 1);
        _categories.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(category);
        _categories.ListLinksAsync(Arg.Any<CancellationToken>()).Returns([new CategoryLink(1, null)]);

        var result = await CreateHandler().Handle(
            new UpdateCategoryCommand(1, "elec", Input("إلكترونيات جديد", "Electronics"), SortOrder: 3, IsActive: false),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        category.NameIn("ar").Should().Be("إلكترونيات جديد");
        category.NameIn("en").Should().Be("Electronics");
        category.Slug.Should().Be("elec");
        category.SortOrder.Should().Be(3);
        category.IsActive.Should().BeFalse();
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

        result.ErrorCode.Should().Be("NotFound");
    }

    [Fact]
    public async Task فئة_تحتوي_منتجات_لا_تُحذف()
    {
        _categories.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(Category("إلكترونيات", "electronics"));
        _products.ExistsInCategoryAsync(1, Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateHandler().Handle(new DeleteCategoryCommand(1), CancellationToken.None);

        result.ErrorCode.Should().Be("CategoryInUse");
    }

    [Fact]
    public async Task فئة_لها_فئات_فرعية_لا_تُحذف()
    {
        _categories.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(Category("إلكترونيات", "electronics"));
        _products.ExistsInCategoryAsync(1, Arg.Any<CancellationToken>()).Returns(false);
        _categories.HasChildrenAsync(1, Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateHandler().Handle(new DeleteCategoryCommand(1), CancellationToken.None);

        result.ErrorCode.Should().Be("CategoryHasChildren");
    }

    [Fact]
    public async Task حذف_صالح_ينجح()
    {
        _categories.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(Category("إلكترونيات", "electronics"));

        var result = await CreateHandler().Handle(new DeleteCategoryCommand(1), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
