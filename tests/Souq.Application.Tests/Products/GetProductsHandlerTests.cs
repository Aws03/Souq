using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Souq.Application.Common.Models;
using Souq.Application.Features.Products.Contracts;
using Souq.Application.Features.Products.Queries;
using Souq.Application.Tests.TestDoubles;

namespace Souq.Application.Tests.Products;

// المعالج يترجم الطلب إلى معايير مطبوعة + صفحة + لغة المتجر ويمرّرها لمنفذ القراءة كما هي. سلوك SQL نفسه
// (التصفية، الترتيب الحتمي، الإسقاط) يُثبَت على SQL Server في اختبارات التكامل.
public class GetProductsHandlerTests
{
    private readonly ICatalogQueries _catalog = Substitute.For<ICatalogQueries>();
    private readonly ISearchLog _log = Substitute.For<ISearchLog>();

    [Fact]
    public async Task يمرّر_كل_الفلاتر_والترتيب_والصفحة_ولغة_المتجر_لمنفذ_القراءة()
    {
        var categories = new List<int> { 1, 2 };
        var page = new ProductSearchPage(new PaginatedList<ProductDto>(new List<ProductDto>(), 25, 2, 12));
        _catalog.SearchProductsAsync(Arg.Any<ProductSearch>(), Arg.Any<PageRequest>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(page);

        var result = await new GetProductsHandler(_catalog, TestTenant.Context(), _log, NullLogger<GetProductsHandler>.Instance).Handle(
            new GetProductsQuery("سما", categories, Page: 2, PageSize: 12,
                MinPrice: 10m, MaxPrice: 100m, SortBy: ProductSortBy.PriceAsc, OnSale: true),
            CancellationToken.None);

        result.Should().BeSameAs(page);
        await _catalog.Received(1).SearchProductsAsync(
            Arg.Is<ProductSearch>(s => s.Keyword == "سما" && s.CategoryIds == categories && s.OnSaleOnly
                                       && s.MinPrice == 10m && s.MaxPrice == 100m && s.SortBy == ProductSortBy.PriceAsc),
            new PageRequest(2, 12), "ar", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task بلا_فلاتر_يستخدم_الافتراضيات_والترتيب_الأحدث()
    {
        _catalog.SearchProductsAsync(Arg.Any<ProductSearch>(), Arg.Any<PageRequest>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ProductSearchPage(new PaginatedList<ProductDto>(new List<ProductDto>(), 0, 1, 12)));

        await new GetProductsHandler(_catalog, TestTenant.Context(), _log, NullLogger<GetProductsHandler>.Instance).Handle(new GetProductsQuery(), CancellationToken.None);

        await _catalog.Received(1).SearchProductsAsync(new ProductSearch(), new PageRequest(1, 12), "ar", Arg.Any<CancellationToken>());
    }

    // ── سجلّ البحث (M13) ────────────────────────────────────────────────────────

    // العدد المُسجَّل هو **الإجمالي** لا عدد عناصر الصفحة: صفحةٌ رابعة فارغة من نتائجٍ كثيرة ليست بحثاً بلا
    // نتيجة، ولو سُجِّلت كذلك لأرسلت التاجر يُصلح كلمةً تعمل.
    [Fact]
    public async Task يُسجَّل_البحث_بإجمالي_النتائج_لا_بعدد_عناصر_الصفحة()
    {
        _catalog.SearchProductsAsync(Arg.Any<ProductSearch>(), Arg.Any<PageRequest>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ProductSearchPage(new PaginatedList<ProductDto>(new List<ProductDto>(), 25, 4, 12)));

        await new GetProductsHandler(_catalog, TestTenant.Context(), _log, NullLogger<GetProductsHandler>.Instance).Handle(
            new GetProductsQuery("مكنسة", Page: 4), CancellationToken.None);

        _log.Received(1).Record("مكنسة", 25, "ar");
    }

    [Fact]
    public async Task بحثٌ_بلا_نتيجة_يُسجَّل_صفراً()
    {
        _catalog.SearchProductsAsync(Arg.Any<ProductSearch>(), Arg.Any<PageRequest>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ProductSearchPage(new PaginatedList<ProductDto>(new List<ProductDto>(), 0, 1, 12)));

        await new GetProductsHandler(_catalog, TestTenant.Context(), _log, NullLogger<GetProductsHandler>.Instance).Handle(
            new GetProductsQuery("شيء لا يوجد"), CancellationToken.None);

        _log.Received(1).Record("شيء لا يوجد", 0, "ar");
    }

    // التصفّح بالفئة أو السعر ليس استعلاماً: صفٌّ له يُثقل الجدول ولا يخبر التاجر بشيء.
    [Fact]
    public async Task التصفّح_بلا_كلمة_لا_يُسجَّل()
    {
        _catalog.SearchProductsAsync(Arg.Any<ProductSearch>(), Arg.Any<PageRequest>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ProductSearchPage(new PaginatedList<ProductDto>(new List<ProductDto>(), 30, 1, 12)));

        await new GetProductsHandler(_catalog, TestTenant.Context(), _log, NullLogger<GetProductsHandler>.Instance).Handle(
            new GetProductsQuery(CategoryIds: [3], MinPrice: 10m), CancellationToken.None);

        await new GetProductsHandler(_catalog, TestTenant.Context(), _log, NullLogger<GetProductsHandler>.Instance).Handle(
            new GetProductsQuery("   "), CancellationToken.None);

        _log.DidNotReceive().Record(Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<string>());
    }

    // ============================================================================
    // الشرط الذي تقوم عليه المرحلة: **فشل التسجيل ليس فشل بحث**. المُسجِّل الحقيقي لا يرمي أبداً، لكن
    // ضمانُ ذلك في تنفيذٍ واحد ليس ضماناً في العقد — فلو استُبدل يوماً بتنفيذٍ يرمي، وجب أن يظلّ المتسوّق
    // يرى نتائجه. هذا الاختبار يثبّت الترتيب: البحث يُعاد أولاً، والتسجيل لا يملك إفشاله.
    // ============================================================================
    [Fact]
    public async Task فشل_التسجيل_لا_يُفشل_البحث()
    {
        var page = new ProductSearchPage(new PaginatedList<ProductDto>(new List<ProductDto>(), 3, 1, 12));
        _catalog.SearchProductsAsync(Arg.Any<ProductSearch>(), Arg.Any<PageRequest>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(page);
        _log.When(l => l.Record(Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<string>()))
            .Do(_ => throw new InvalidOperationException("سجلّ معطوب"));

        var act = async () => await new GetProductsHandler(_catalog, TestTenant.Context(), _log, NullLogger<GetProductsHandler>.Instance).Handle(
            new GetProductsQuery("مكنسة"), CancellationToken.None);

        (await act.Should().NotThrowAsync()).Which.Should().BeSameAs(page);
    }
}
