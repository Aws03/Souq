using Microsoft.EntityFrameworkCore;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;

namespace Souq.Infrastructure.Persistence.Repositories;

public class CategoryRepository : RepositoryBase<Category>, ICategoryRepository
{
    public CategoryRepository(AppDbContext db) : base(db) { }

    public override Task<Category?> GetByIdAsync(int id, CancellationToken ct = default) =>
        Db.Categories.Include(c => c.Translations).FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<Category?> GetBySlugAsync(string slug, CancellationToken ct = default)
        => Db.Categories.Include(c => c.Translations).FirstOrDefaultAsync(c => c.Slug == slug, ct);

    public Task<bool> HasChildrenAsync(int parentId, CancellationToken ct = default)
        => Db.Categories.AnyAsync(c => c.ParentId == parentId, ct);

    public async Task<IReadOnlyList<CategoryLink>> ListLinksAsync(CancellationToken ct = default)
        => await Db.Categories.AsNoTracking().Select(c => new CategoryLink(c.Id, c.ParentId)).ToListAsync(ct);
}
