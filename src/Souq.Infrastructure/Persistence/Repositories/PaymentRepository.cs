using Microsoft.EntityFrameworkCore;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;

namespace Souq.Infrastructure.Persistence.Repositories;

// الدفعات لجهة الكتابة (المرحلة 11). Reset ينسى الدفعات والاستردادات المتتبَّعة بعد تعارض (نمط استخدامات الكوبونات).
public class PaymentRepository : RepositoryBase<Payment>, IPaymentRepository
{
    public PaymentRepository(AppDbContext db) : base(db) { }

    public Task<Payment?> GetForOrderAsync(int orderId, CancellationToken ct = default) =>
        Db.Payments.Include(p => p.Refunds).FirstOrDefaultAsync(p => p.OrderId == orderId, ct);

    public Task<PaymentAccountRef?> GetAccountAsync(string providerPaymentId, CancellationToken ct = default) =>
        Db.Payments.Where(p => p.ProviderPaymentId == providerPaymentId)
            .Select(p => new PaymentAccountRef(p.Gateway, p.GatewayAccount))
            .FirstOrDefaultAsync(ct);

    public void Reset()
    {
        foreach (var entry in Db.ChangeTracker.Entries().Where(e => e.Entity is Payment or Refund).ToList())
            entry.State = EntityState.Detached;
    }
}
