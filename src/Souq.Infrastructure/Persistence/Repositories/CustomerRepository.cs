using Microsoft.EntityFrameworkCore;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;

namespace Souq.Infrastructure.Persistence.Repositories;

public class CustomerRepository : RepositoryBase<Customer>, ICustomerRepository
{
    public CustomerRepository(AppDbContext db) : base(db) { }

    public Task<Customer?> GetByUserIdAsync(int userId, CancellationToken ct = default) =>
        Db.Customers.FirstOrDefaultAsync(c => c.UserId == userId, ct);

    public async Task<int?> FindIdByUserIdAsync(int userId, CancellationToken ct = default) =>
        await Db.Customers.Where(c => c.UserId == userId).Select(c => (int?)c.Id).FirstOrDefaultAsync(ct);
}
