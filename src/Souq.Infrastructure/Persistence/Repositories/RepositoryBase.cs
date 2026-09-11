using Microsoft.EntityFrameworkCore;
using Souq.Domain.Common;
using Souq.Domain.Interfaces;

namespace Souq.Infrastructure.Persistence.Repositories;

// ============================================================================
// التنفيذ الفعلي لـ IRepository<T> باستخدام EF Core. هنا — وهنا فقط — يعيش كود قاعدة
// البيانات لجهة الكتابة. الكيانات المُعادة متتبَّعة: تعديلها ثم IUnitOfWork.SaveChangesAsync
// يكتب الأعمدة المتغيّرة فقط (لا Update() صريح — Phase 0 D7).
// ============================================================================
public class RepositoryBase<T> : IRepository<T> where T : Entity
{
    protected readonly AppDbContext Db;
    public RepositoryBase(AppDbContext db) => Db = db;

    // افتراضياً الجذر وحده؛ تجمّع له أبناء تحتاجهم قواعده (منتج، فئة) يحمّلهم في مستودعه.
    public virtual async Task<T?> GetByIdAsync(int id, CancellationToken ct = default)
        => await Db.Set<T>().FindAsync(new object[] { id }, ct);

    public async Task AddAsync(T entity, CancellationToken ct = default)
        => await Db.Set<T>().AddAsync(entity, ct);

    public void Remove(T entity) => Db.Set<T>().Remove(entity);
}
