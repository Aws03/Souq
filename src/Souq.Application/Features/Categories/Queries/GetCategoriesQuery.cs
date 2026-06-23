using MediatR;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Categories.Queries;

public record CategoryDto(int Id, string Name, string Slug, int? ParentId);
public record GetCategoriesQuery() : IRequest<List<CategoryDto>>;

public class GetCategoriesHandler : IRequestHandler<GetCategoriesQuery, List<CategoryDto>>
{
    private readonly ICategoryRepository _categories;
    public GetCategoriesHandler(ICategoryRepository categories) => _categories = categories;

    public async Task<List<CategoryDto>> Handle(GetCategoriesQuery q, CancellationToken ct)
    {
        var all = await _categories.ListAllAsync(ct);
        return all.Select(c => new CategoryDto(c.Id, c.Name, c.Slug, c.ParentId)).ToList();
    }
}
