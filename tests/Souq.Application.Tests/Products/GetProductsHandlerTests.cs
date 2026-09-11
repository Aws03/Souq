using AwesomeAssertions;
using NSubstitute;
using Souq.Application.Common.Models;
using Souq.Application.Features.Products.Queries;

namespace Souq.Application.Tests.Products;

// المعالج يترجم الطلب إلى معايير مطبوعة + صفحة ويمرّرها لمنفذ القراءة كما هي. سلوك SQL
// نفسه (التصفية، الترتيب الحتمي، الإسقاط) يُثبَت على SQL Server في اختبارات التكامل.
public class GetProductsHandlerTests
{
    private readonly ICatalogQueries _catalog = Substitute.For<ICatalogQueries>();

    [Fact]
    public async Task يمرّر_كل_الفلاتر_والترتيب_والصفحة_لمنفذ_القراءة_كما_هي()
    {
        var categories = new List<int> { 1, 2 };
        var page = new PaginatedList<ProductDto>(new List<ProductDto>(), 25, 2, 12);
        _catalog.SearchProductsAsync(Arg.Any<ProductSearch>(), Arg.Any<PageRequest>(), Arg.Any<CancellationToken>())
            .Returns(page);

        var result = await new GetProductsHandler(_catalog).Handle(
            new GetProductsQuery("سما", categories, Page: 2, PageSize: 12,
                MinPrice: 10m, MaxPrice: 100m, SortBy: ProductSortBy.PriceAsc),
            CancellationToken.None);

        result.Should().BeSameAs(page);
        await _catalog.Received(1).SearchProductsAsync(
            Arg.Is<ProductSearch>(s => s.Keyword == "سما" && s.CategoryIds == categories
                                       && s.MinPrice == 10m && s.MaxPrice == 100m && s.SortBy == ProductSortBy.PriceAsc),
            new PageRequest(2, 12), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task بلا_فلاتر_يستخدم_الافتراضيات_والترتيب_الأحدث()
    {
        _catalog.SearchProductsAsync(Arg.Any<ProductSearch>(), Arg.Any<PageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PaginatedList<ProductDto>(new List<ProductDto>(), 0, 1, 12));

        await new GetProductsHandler(_catalog).Handle(new GetProductsQuery(), CancellationToken.None);

        await _catalog.Received(1).SearchProductsAsync(new ProductSearch(), new PageRequest(1, 12), Arg.Any<CancellationToken>());
    }
}
