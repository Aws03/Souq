using MediatR;
using Souq.Application.Common.Models;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Categories.Commands;

public record CreateCategoryCommand(string Name, string Slug, int? ParentId = null)
    : IRequest<Result<int>>;

public class CreateCategoryHandler : IRequestHandler<CreateCategoryCommand, Result<int>>
{
    private readonly ICategoryRepository _categories;
    private readonly IUnitOfWork _uow;

    public CreateCategoryHandler(ICategoryRepository categories, IUnitOfWork uow)
    {
        _categories = categories; _uow = uow;
    }

    public async Task<Result<int>> Handle(CreateCategoryCommand cmd, CancellationToken ct)
    {
        var slug = cmd.Slug.Trim().ToLowerInvariant();

        // الـ slug فريد (قيد فريد في القاعدة أيضاً) — فحص مبكر لرسالة واضحة.
        if (await _categories.GetBySlugAsync(slug, ct) is not null)
            return Result<int>.Failure("المُعرّف (slug) مستخدم مسبقاً", "SlugTaken");

        if (cmd.ParentId is not null && await _categories.GetByIdAsync(cmd.ParentId.Value, ct) is null)
            return Result<int>.Failure("الفئة الأب غير موجودة", "ParentNotFound");

        var category = new Category(cmd.Name.Trim(), slug, cmd.ParentId);
        await _categories.AddAsync(category, ct);
        await _uow.SaveChangesAsync(ct);
        return Result<int>.Success(category.Id);
    }
}
