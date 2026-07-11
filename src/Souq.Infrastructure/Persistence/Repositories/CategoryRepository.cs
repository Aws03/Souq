using Microsoft.EntityFrameworkCore;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;

namespace Souq.Infrastructure.Persistence.Repositories;

public class CategoryRepository : RepositoryBase<Category>, ICategoryRepository
{
    public CategoryRepository(AppDbContext db) : base(db) { }

    public async Task<Category?> GetBySlugAsync(string slug, CancellationToken ct = default)
        => await Db.Categories.FirstOrDefaultAsync(c => c.Slug == slug, ct);

    public async Task<bool> HasChildrenAsync(int parentId, CancellationToken ct = default)
        => await Db.Categories.AnyAsync(c => c.ParentId == parentId, ct);
}
