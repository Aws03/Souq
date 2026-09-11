using MediatR;
using Souq.Application.Features.Products.Queries;

namespace Souq.Application.Features.Categories.Queries;

public record CategoryDto(int Id, string Name, string Slug, int? ParentId);

// شجرة فئات متجر واحد صغيرة بطبيعتها (عشرات) — تُعاد كاملة، مُسقطة بلا تتبّع عبر منفذ قراءة
// وحدة Catalog. (IRepository.ListAllAsync العام أُزيل: فخّ يحمّل أي جدول كاملاً.)
public record GetCategoriesQuery() : IRequest<IReadOnlyList<CategoryDto>>;

public class GetCategoriesHandler : IRequestHandler<GetCategoriesQuery, IReadOnlyList<CategoryDto>>
{
    private readonly ICatalogQueries _catalog;
    public GetCategoriesHandler(ICatalogQueries catalog) => _catalog = catalog;

    public Task<IReadOnlyList<CategoryDto>> Handle(GetCategoriesQuery q, CancellationToken ct) =>
        _catalog.ListCategoriesAsync(ct);
}
