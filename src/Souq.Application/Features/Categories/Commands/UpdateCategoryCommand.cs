using MediatR;
using Souq.Application.Common.Models;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Categories.Commands;

// Id يفرضه الـ Controller من المسار (لا من الجسم) — نفس نمط تحديث المنتج.
public record UpdateCategoryCommand(int Id, string Name, string Slug, int? ParentId = null)
    : IRequest<Result>;

public class UpdateCategoryHandler : IRequestHandler<UpdateCategoryCommand, Result>
{
    private readonly ICategoryRepository _categories;
    private readonly IUnitOfWork _uow;

    public UpdateCategoryHandler(ICategoryRepository categories, IUnitOfWork uow)
    {
        _categories = categories; _uow = uow;
    }

    public async Task<Result> Handle(UpdateCategoryCommand cmd, CancellationToken ct)
    {
        var category = await _categories.GetByIdAsync(cmd.Id, ct);
        if (category is null)
            return Result.Failure("الفئة غير موجودة", "NotFound");

        var slug = cmd.Slug.Trim().ToLowerInvariant();

        // الـ slug فريد باستثناء الفئة نفسها.
        var bySlug = await _categories.GetBySlugAsync(slug, ct);
        if (bySlug is not null && bySlug.Id != cmd.Id)
            return Result.Failure("المُعرّف (slug) مستخدم مسبقاً", "SlugTaken");

        if (cmd.ParentId is not null)
        {
            if (cmd.ParentId == cmd.Id)
                return Result.Failure("لا يمكن أن تكون الفئة أباً لنفسها", "InvalidParent");
            if (await _categories.GetByIdAsync(cmd.ParentId.Value, ct) is null)
                return Result.Failure("الفئة الأب غير موجودة", "ParentNotFound");
        }

        category.UpdateDetails(cmd.Name.Trim(), slug, cmd.ParentId);
        _categories.Update(category);
        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
