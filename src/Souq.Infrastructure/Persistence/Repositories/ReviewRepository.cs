using Microsoft.EntityFrameworkCore;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;

namespace Souq.Infrastructure.Persistence.Repositories;

public class ReviewRepository : RepositoryBase<Review>, IReviewRepository
{
    public ReviewRepository(AppDbContext db) : base(db) { }

    public async Task<(IReadOnlyList<Review> Items, int TotalCount, double AverageRating)> GetByProductAsync(
        int productId, int page, int pageSize, CancellationToken ct = default)
    {
        var query = Db.Reviews.Where(r => r.ProductId == productId);
        var total = await query.CountAsync(ct);
        var average = total == 0 ? 0 : await query.AverageAsync(r => (double)r.Rating, ct);
        var items = await query.OrderByDescending(r => r.CreatedAt)
                               .Skip((page - 1) * pageSize).Take(pageSize)
                               .ToListAsync(ct);
        return (items, total, average);
    }

    public async Task<bool> HasCustomerReviewedProductAsync(int customerId, int productId, CancellationToken ct = default)
        => await Db.Reviews.AnyAsync(r => r.CustomerId == customerId && r.ProductId == productId, ct);
}
