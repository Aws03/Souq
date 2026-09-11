using FluentValidation;
using MediatR;
using Souq.Application.Common.Models;
using Souq.Application.Common.Tenancy;
using Souq.Domain.Enums;

namespace Souq.Application.Features.Products.Queries;

// ============================================================================
// منتجات الإدارة (المرحلة 5، catalog.manage): كل الحالات — المسودّة والمؤرشف يظهران هنا ليُنشرا أو يُستعادا (C7:
// كان المنتج المعطّل يختفي من الإدارة أيضاً فلا يُستعاد). بحث بالاسم بأي لغة أو SKU أو المعرّف، وترتيب مسموح.
// ============================================================================
public record ListAdminProductsQuery(
    string? Keyword = null, ProductStatus? Status = null, int? CategoryId = null,
    AdminProductSortBy SortBy = AdminProductSortBy.Newest, int Page = 1, int PageSize = 20)
    : IRequest<PaginatedList<AdminProductListItemDto>>, IPagedQuery;

public sealed class ListAdminProductsQueryValidator : PagedQueryValidator<ListAdminProductsQuery>
{
    public ListAdminProductsQueryValidator()
    {
        RuleFor(x => x.Keyword).MaximumLength(200);
        RuleFor(x => x.Status).IsInEnum().When(x => x.Status.HasValue);
        RuleFor(x => x.CategoryId).GreaterThan(0).When(x => x.CategoryId.HasValue);
        RuleFor(x => x.SortBy).IsInEnum();
    }
}

public class ListAdminProductsHandler : IRequestHandler<ListAdminProductsQuery, PaginatedList<AdminProductListItemDto>>
{
    private readonly ICatalogQueries _catalog;
    private readonly ITenantContext _tenant;

    public ListAdminProductsHandler(ICatalogQueries catalog, ITenantContext tenant)
    {
        _catalog = catalog; _tenant = tenant;
    }

    public Task<PaginatedList<AdminProductListItemDto>> Handle(ListAdminProductsQuery q, CancellationToken ct) =>
        _catalog.ListAdminProductsAsync(
            new AdminProductSearch(q.Keyword?.Trim(), q.Status, q.CategoryId, q.SortBy),
            PageRequest.From(q), _tenant.RequireTenant().DefaultCulture, ct);
}

public record GetAdminProductQuery(int Id) : IRequest<Result<AdminProductDto>>;

public class GetAdminProductHandler : IRequestHandler<GetAdminProductQuery, Result<AdminProductDto>>
{
    private readonly ICatalogQueries _catalog;
    public GetAdminProductHandler(ICatalogQueries catalog) => _catalog = catalog;

    public async Task<Result<AdminProductDto>> Handle(GetAdminProductQuery q, CancellationToken ct) =>
        await _catalog.FindAdminProductAsync(q.Id, ct) is { } product
            ? Result<AdminProductDto>.Success(product)
            : Result<AdminProductDto>.Failure(Error.NotFound("المنتج غير موجود"));
}
