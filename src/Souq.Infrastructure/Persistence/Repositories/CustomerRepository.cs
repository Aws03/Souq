using Microsoft.EntityFrameworkCore;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;

namespace Souq.Infrastructure.Persistence.Repositories;

public class CustomerRepository : RepositoryBase<Customer>, ICustomerRepository
{
    public CustomerRepository(AppDbContext db) : base(db) { }

    public async Task<Customer?> GetByEmailAsync(string email, CancellationToken ct = default)
        => await Db.Customers.FirstOrDefaultAsync(c => c.Email == email, ct);

    // القاعدة تحفظ تجزئة الرمز فقط — نجزّئ الرمز الوارد بنفس قاعدة الكيان ونبحث بها.
    public async Task<Customer?> GetByResetTokenAsync(string token, CancellationToken ct = default)
    {
        var hash = Customer.HashResetToken(token);
        return await Db.Customers.FirstOrDefaultAsync(c => c.PasswordResetTokenHash == hash, ct);
    }
}
