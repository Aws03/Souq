using MediatR;
using Souq.Application.Common.Auditing;
using Souq.Application.Common.Models;
using Souq.Application.Common.Tenancy;
using Souq.Application.Features.Products.Queries;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Categories.Commands;

// فئة جديدة (المرحلة 5): نصوص لكل لغة (لغة المتجر الافتراضية شرط)، معرّف رابط فريد في المتجر، أب اختياري داخل
// حدود العمق، ترتيب عرض، وعلَم تفعيل.
public record CreateCategoryCommand(
    string Slug, IReadOnlyDictionary<string, CatalogTextInput> Translations,
    int? ParentId = null, int SortOrder = 0, bool IsActive = true) : IRequest<Result<int>>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("catalog.category.created", "Category", Slug?.Trim().ToLowerInvariant(),
        Metadata: new Dictionary<string, object?> { ["parentId"] = ParentId });
}

public class CreateCategoryHandler : IRequestHandler<CreateCategoryCommand, Result<int>>
{
    private readonly ICategoryRepository _categories;
    private readonly ITenantContext _tenant;
    private readonly IUnitOfWork _uow;

    public CreateCategoryHandler(ICategoryRepository categories, ITenantContext tenant, IUnitOfWork uow)
    {
        _categories = categories; _tenant = tenant; _uow = uow;
    }

    public async Task<Result<int>> Handle(CreateCategoryCommand cmd, CancellationToken ct)
    {
        var culture = _tenant.RequireTenant().DefaultCulture;
        if (!CatalogTexts.HasCulture(cmd.Translations, culture))
            return Result<int>.Failure(CategoryRules.DefaultTranslationRequired(culture));

        var category = new Category(cmd.Slug, CatalogTexts.ToDomain(cmd.Translations), cmd.SortOrder);
        if (!cmd.IsActive) category.Deactivate();

        // الـ slug فريد في المتجر (قيد فريد في القاعدة أيضاً) — فحص مبكر لرسالة واضحة.
        if (await _categories.GetBySlugAsync(category.Slug, ct) is not null)
            return Result<int>.Failure(CategoryRules.SlugTaken);

        if (cmd.ParentId is int parentId)
        {
            var links = await _categories.ListLinksAsync(ct);
            if (links.All(l => l.Id != parentId))
                return Result<int>.Failure(CategoryRules.ParentNotFound);
            category.MoveTo(parentId, CategoryTree.AncestryOf(parentId, links));
        }

        await _categories.AddAsync(category, ct);
        await _uow.SaveChangesAsync(ct);
        return Result<int>.Success(category.Id);
    }
}

internal static class CategoryRules
{
    public static Error SlugTaken => Error.Conflict("SlugTaken", "المُعرّف (slug) مستخدم مسبقاً");
    public static Error ParentNotFound => Error.Validation("ParentNotFound", "الفئة الأب غير موجودة");

    public static Error DefaultTranslationRequired(string culture) =>
        Error.Validation("DefaultTranslationRequired", $"اسم الفئة بلغة المتجر الافتراضية ({culture}) مطلوب");
}

// أسئلة الشجرة من هيكلها (معرّف ⇒ أب) المقروء مرّة واحدة. تتوقّف عند حلقة قائمة بدل الدوران للأبد.
public static class CategoryTree
{
    // الأب ثم أسلافه صعوداً حتى الجذر.
    public static IReadOnlyList<int> AncestryOf(int parentId, IReadOnlyList<CategoryLink> links)
    {
        var parents = links.ToDictionary(l => l.Id, l => l.ParentId);
        var chain = new List<int>();
        for (int? current = parentId; current is int id && !chain.Contains(id); current = parents.GetValueOrDefault(id))
            chain.Add(id);
        return chain;
    }

    // عمق الفئة مع فروعها (ورقة = 1) — نقل فرع كامل لا يتجاوز حدّ العمق.
    public static int SubtreeHeight(int categoryId, IReadOnlyList<CategoryLink> links)
    {
        var children = links.Where(l => l.ParentId is not null).ToLookup(l => l.ParentId!.Value, l => l.Id);
        int Height(int id, int guard) => guard > Category.MaxDepth * 4 ? 1
            : 1 + children[id].Select(child => Height(child, guard + 1)).DefaultIfEmpty(0).Max();
        return Height(categoryId, 0);
    }
}
