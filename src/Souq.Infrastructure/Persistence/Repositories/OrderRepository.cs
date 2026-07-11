using Microsoft.EntityFrameworkCore;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;

namespace Souq.Infrastructure.Persistence.Repositories;

public class OrderRepository : RepositoryBase<Order>, IOrderRepository
{
    public OrderRepository(AppDbContext db) : base(db) { }

    // نجلب الطلب مع أسطره (التجمّع كاملاً) عبر Include.
    public async Task<Order?> GetWithItemsAsync(int id, CancellationToken ct = default)
        => await Db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == id, ct);

    public async Task<IReadOnlyList<Order>> GetByCustomerAsync(int customerId, CancellationToken ct = default)
        => await Db.Orders.Include(o => o.Items)
                          .Where(o => o.CustomerId == customerId)
                          .OrderByDescending(o => o.CreatedAt).ToListAsync(ct);

    public async Task<(IReadOnlyList<Order> Items, int TotalCount)> GetPagedAsync(
        int page, int pageSize, CancellationToken ct = default)
    {
        var total = await Db.Orders.CountAsync(ct);
        var items = await Db.Orders.Include(o => o.Items)
                                   .OrderByDescending(o => o.CreatedAt)
                                   .Skip((page - 1) * pageSize).Take(pageSize)
                                   .ToListAsync(ct);
        return (items, total);
    }
}
