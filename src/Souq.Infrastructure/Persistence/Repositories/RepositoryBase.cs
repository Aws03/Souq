using Microsoft.EntityFrameworkCore;
using Souq.Domain.Common;
using Souq.Domain.Interfaces;

namespace Souq.Infrastructure.Persistence.Repositories;

// ============================================================================
// التنفيذ الفعلي لـ IRepository<T> باستخدام EF Core. هنا — وهنا فقط — يعيش
// كود قاعدة البيانات. المعالجات في Application لا تراه؛ ترى الواجهة فقط.
// هذا هو الوفاء بالوعد الذي قطعه Domain عبر الواجهة.
// ============================================================================
public class RepositoryBase<T> : IRepository<T> where T : Entity
{
    protected readonly AppDbContext Db;
    public RepositoryBase(AppDbContext db) => Db = db;

    public async Task<T?> GetByIdAsync(int id, CancellationToken ct = default)
        => await Db.Set<T>().FindAsync(new object[] { id }, ct);

    public async Task<IReadOnlyList<T>> ListAllAsync(CancellationToken ct = default)
        => await Db.Set<T>().ToListAsync(ct);

    public async Task AddAsync(T entity, CancellationToken ct = default)
        => await Db.Set<T>().AddAsync(entity, ct);

    public void Update(T entity) => Db.Set<T>().Update(entity);
    public void Remove(T entity) => Db.Set<T>().Remove(entity);
}
