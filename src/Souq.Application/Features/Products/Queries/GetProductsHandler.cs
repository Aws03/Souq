using MediatR;
using Souq.Application.Common.Models;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Products.Queries;

// معالج الاستعلام: يحتوي منطق التنسيق فقط (ينادي المستودع ويحوّل النتيجة لـ DTO).
// لا يعرف شيئاً عن HTTP ولا عن SQL — يعتمد على واجهة المستودع فقط.
public class GetProductsHandler : IRequestHandler<GetProductsQuery, PaginatedList<ProductDto>>
{
    private readonly IProductRepository _products;

    // حقن التبعية (Dependency Injection): نستقبل المستودع جاهزاً، لا نُنشئه بأنفسنا.
    // هذا يجعل المعالج قابلاً للاختبار (نمرّر مستودعاً وهمياً في الاختبار).
    public GetProductsHandler(IProductRepository products) => _products = products;

    public async Task<PaginatedList<ProductDto>> Handle(GetProductsQuery q, CancellationToken ct)
    {
        var (items, total) = await _products.SearchAsync(q.Keyword, q.CategoryId, q.Page, q.PageSize, ct);

        var dtos = items.Select(p => new ProductDto(
            p.Id, p.Name, p.Description,
            p.Price.Amount, p.Price.Currency,
            p.StockQuantity, p.ImageUrl,
            p.CategoryId, p.Category?.Name)).ToList();

        return new PaginatedList<ProductDto>(dtos, total, q.Page, q.PageSize);
    }
}
