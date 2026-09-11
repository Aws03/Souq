using Microsoft.EntityFrameworkCore;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;

namespace Souq.Infrastructure.Persistence.Repositories;

public class ReviewRepository : RepositoryBase<Review>, IReviewRepository
{
    public ReviewRepository(AppDbContext db) : base(db) { }

    public async Task<bool> HasCustomerReviewedProductAsync(int customerId, int productId, CancellationToken ct = default)
        => await Db.Reviews.AnyAsync(r => r.CustomerId == customerId && r.ProductId == productId, ct);
}
