using MediatR;
using Souq.Application.Common.Tenancy;
using Souq.Application.Features.Products.Queries;

namespace Souq.Application.Features.Categories.Queries;

// فئة بنصوصها لكل لغة + اسمها بلغة المتجر الافتراضية، وموقعها في الشجرة وترتيبها.
public sealed record CategoryDto(
    int Id, string Slug, string Name, IReadOnlyDictionary<string, CatalogTextDto> Translations,
    int? ParentId, int SortOrder, bool IsActive);

// شجرة فئات متجر واحد صغيرة بطبيعتها (عشرات) — تُعاد كاملة، مرتّبة (الترتيب ثم الاسم). المتجر: المفعّلة فقط.
public record GetCategoriesQuery() : IRequest<IReadOnlyList<CategoryDto>>;

// الإدارة: كل الفئات بما فيها المعطّلة.
public record ListAdminCategoriesQuery() : IRequest<IReadOnlyList<CategoryDto>>;

public class GetCategoriesHandler :
    IRequestHandler<GetCategoriesQuery, IReadOnlyList<CategoryDto>>,
    IRequestHandler<ListAdminCategoriesQuery, IReadOnlyList<CategoryDto>>
{
    private readonly ICatalogQueries _catalog;
    private readonly ITenantContext _tenant;

    public GetCategoriesHandler(ICatalogQueries catalog, ITenantContext tenant)
    {
        _catalog = catalog; _tenant = tenant;
    }

    public Task<IReadOnlyList<CategoryDto>> Handle(GetCategoriesQuery q, CancellationToken ct) =>
        _catalog.ListCategoriesAsync(includeInactive: false, _tenant.RequireTenant().DefaultCulture, ct);

    public Task<IReadOnlyList<CategoryDto>> Handle(ListAdminCategoriesQuery q, CancellationToken ct) =>
        _catalog.ListCategoriesAsync(includeInactive: true, _tenant.RequireTenant().DefaultCulture, ct);
}
