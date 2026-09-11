using AwesomeAssertions;
using NSubstitute;
using Souq.Application.Features.Products.Queries;

namespace Souq.Application.Tests.Products;

// استبعاد المنتج نفسه وأولوية نفس الفئة سلوك SQL في CatalogQueries (اختبار تكامل)؛ هنا
// عقد المعالج: غير موجود ⇒ 404، والعدد يُمرَّر كما طُلب.
public class GetRelatedProductsHandlerTests
{
    private readonly ICatalogQueries _catalog = Substitute.For<ICatalogQueries>();

    private GetRelatedProductsHandler CreateHandler() => new(_catalog);

    private static ProductDto Dto(int id) => new(id, "سماعات", "Headphones", "وصف", 50, "JOD", 10, "img", null, 3, "إلكترونيات");

    [Fact]
    public async Task منتج_غير_موجود_أو_معطّل_يُرجع_NotFound()
    {
        _catalog.FindRelatedProductsAsync(1, 6, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<ProductDto>?)null);

        var result = await CreateHandler().Handle(new GetRelatedProductsQuery(1), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("NotFound");
    }

    [Fact]
    public async Task يُعيد_المنتجات_ذات_الصلة_بالعدد_الافتراضي()
    {
        _catalog.FindRelatedProductsAsync(1, 6, Arg.Any<CancellationToken>()).Returns(new List<ProductDto> { Dto(2) });

        var result = await CreateHandler().Handle(new GetRelatedProductsQuery(1), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle().Which.Id.Should().Be(2);
    }

    [Fact]
    public async Task يمرّر_العدد_المطلوب_لمنفذ_القراءة()
    {
        _catalog.FindRelatedProductsAsync(5, 3, Arg.Any<CancellationToken>()).Returns(new List<ProductDto>());

        await CreateHandler().Handle(new GetRelatedProductsQuery(5, 3), CancellationToken.None);

        await _catalog.Received(1).FindRelatedProductsAsync(5, 3, Arg.Any<CancellationToken>());
    }
}
