using MediatR;
using Souq.Application.Common.Auditing;
using Souq.Application.Common.Models;
using Souq.Application.Common.Tenancy;
using Souq.Application.Features.Products.Queries;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Categories.Commands;

// تعديل فئة ونقلها وترتيبها وتفعيلها. Id يفرضه الـ Controller من المسار (لا من الجسم). النقل تحت فرعها أو أعمق من
// الحدّ يرفضه الكيان (InvalidParent، 422).
public record UpdateCategoryCommand(
    int Id, string Slug, IReadOnlyDictionary<string, CatalogTextInput> Translations,
    int? ParentId = null, int SortOrder = 0, bool IsActive = true) : IRequest<Result>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("catalog.category.updated", "Category", Id.ToString(),
        Metadata: new Dictionary<string, object?> { ["parentId"] = ParentId, ["isActive"] = IsActive });
}

public class UpdateCategoryHandler : IRequestHandler<UpdateCategoryCommand, Result>
{
    private readonly ICategoryRepository _categories;
    private readonly ITenantContext _tenant;
    private readonly IUnitOfWork _uow;

    public UpdateCategoryHandler(ICategoryRepository categories, ITenantContext tenant, IUnitOfWork uow)
    {
        _categories = categories; _tenant = tenant; _uow = uow;
    }

    public async Task<Result> Handle(UpdateCategoryCommand cmd, CancellationToken ct)
    {
        var category = await _categories.GetByIdAsync(cmd.Id, ct);
        if (category is null)
            return Result.Failure(Error.NotFound("الفئة غير موجودة"));

        var culture = _tenant.RequireTenant().DefaultCulture;
        if (!CatalogTexts.HasCulture(cmd.Translations, culture))
            return Result.Failure(CategoryRules.DefaultTranslationRequired(culture));

        category.SetSlug(cmd.Slug);
        var bySlug = await _categories.GetBySlugAsync(category.Slug, ct);
        if (bySlug is not null && bySlug.Id != cmd.Id)
            return Result.Failure(CategoryRules.SlugTaken);

        var links = await _categories.ListLinksAsync(ct);
        if (cmd.ParentId is int parentId && links.All(l => l.Id != parentId))
            return Result.Failure(CategoryRules.ParentNotFound);

        category.SetTexts(CatalogTexts.ToDomain(cmd.Translations));
        category.SetSortOrder(cmd.SortOrder);
        category.MoveTo(cmd.ParentId,
            cmd.ParentId is int parent ? CategoryTree.AncestryOf(parent, links) : [],
            CategoryTree.SubtreeHeight(category.Id, links));
        if (cmd.IsActive) category.Activate(); else category.Deactivate();

        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
