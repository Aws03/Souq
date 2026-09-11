using Microsoft.EntityFrameworkCore;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;

namespace Souq.Infrastructure.Persistence.Repositories;

// ملفات العملاء لجهة الكتابة. التجمّع يُحمَّل بدفتر عناوينه (المرحلة 7) — قواعد الافتراضي والحدّ تحتاجه كاملاً.
public class CustomerRepository : RepositoryBase<Customer>, ICustomerRepository
{
    public CustomerRepository(AppDbContext db) : base(db) { }

    public override Task<Customer?> GetByIdAsync(int id, CancellationToken ct = default) =>
        Db.Customers.Include(c => c.Addresses).FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<Customer?> GetByUserIdAsync(int userId, CancellationToken ct = default) =>
        Db.Customers.Include(c => c.Addresses).FirstOrDefaultAsync(c => c.UserId == userId, ct);

    public async Task<int?> FindIdByUserIdAsync(int userId, CancellationToken ct = default) =>
        await Db.Customers.Where(c => c.UserId == userId).Select(c => (int?)c.Id).FirstOrDefaultAsync(ct);
}
