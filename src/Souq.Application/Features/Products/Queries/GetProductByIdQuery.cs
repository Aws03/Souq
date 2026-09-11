using FluentValidation;
using MediatR;
using Souq.Application.Common.Models;
using Souq.Application.Common.Tenancy;
using Souq.Domain.Entities;

namespace Souq.Application.Features.Products.Queries;

// منتج معروض بمعرّفه أو بمعرّف رابطه (slug). غير موجود أو غير معروض (مسودّة/مؤرشف/فئته معطّلة) ⇒ 404 نفسه.
public record GetProductByIdQuery(int Id) : IRequest<Result<ProductDto>>;

public record GetProductBySlugQuery(string Slug) : IRequest<Result<ProductDto>>;

public sealed class GetProductBySlugQueryValidator : AbstractValidator<GetProductBySlugQuery>
{
    public GetProductBySlugQueryValidator() => RuleFor(x => x.Slug).NotEmpty().MaximumLength(Product.SlugMaxLength);
}

public class GetProductByIdHandler : IRequestHandler<GetProductByIdQuery, Result<ProductDto>>, IRequestHandler<GetProductBySlugQuery, Result<ProductDto>>
{
    private readonly ICatalogQueries _catalog;
    private readonly ITenantContext _tenant;

    public GetProductByIdHandler(ICatalogQueries catalog, ITenantContext tenant)
    {
        _catalog = catalog; _tenant = tenant;
    }

    public async Task<Result<ProductDto>> Handle(GetProductByIdQuery q, CancellationToken ct) =>
        Found(await _catalog.FindActiveProductAsync(q.Id, _tenant.RequireTenant().DefaultCulture, ct));

    public async Task<Result<ProductDto>> Handle(GetProductBySlugQuery q, CancellationToken ct) =>
        Found(await _catalog.FindActiveProductBySlugAsync(
            q.Slug.Trim().ToLowerInvariant(), _tenant.RequireTenant().DefaultCulture, ct));

    private static Result<ProductDto> Found(ProductDto? product) => product is null
        ? Result<ProductDto>.Failure(Error.NotFound("المنتج غير موجود"))
        : Result<ProductDto>.Success(product);
}
