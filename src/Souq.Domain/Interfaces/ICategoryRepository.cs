using Souq.Domain.Entities;

namespace Souq.Domain.Interfaces;

// منفذ الكتابة للفئات. GetByIdAsync يحمّل الترجمات. ListLinksAsync: هيكل شجرة المتجر (معرّف ⇒ أب) — صغير
// بطبيعته (عشرات) فيُقرأ كاملاً لحساب الأسلاف والعمق قبل أي نقل (Category.MoveTo).
public interface ICategoryRepository : IRepository<Category>
{
    Task<Category?> GetBySlugAsync(string slug, CancellationToken ct = default);

    Task<bool> HasChildrenAsync(int parentId, CancellationToken ct = default);

    Task<IReadOnlyList<CategoryLink>> ListLinksAsync(CancellationToken ct = default);
}

public readonly record struct CategoryLink(int Id, int? ParentId);
