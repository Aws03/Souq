using Souq.Domain.Entities;
namespace Souq.Domain.Interfaces;

public interface ICategoryRepository : IRepository<Category>
{
    // الـ slug فريد (يُستخدم في الروابط) — نحتاج البحث به لحراسة التفرّد.
    Task<Category?> GetBySlugAsync(string slug, CancellationToken ct = default);

    // هل لهذه الفئة فئات فرعية؟ (لمنع حذف فئة أب لها أبناء).
    Task<bool> HasChildrenAsync(int parentId, CancellationToken ct = default);
}
