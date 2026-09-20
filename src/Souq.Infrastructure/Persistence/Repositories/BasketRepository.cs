using Microsoft.EntityFrameworkCore;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;

namespace Souq.Infrastructure.Persistence.Repositories;

// السلال لجهة الكتابة (المرحلة 8). البحث مُرشَّح بالمتجر كأي كيان ITenantOwned: رمز زائر من متجر آخر لا يطابق شيئاً هنا.
public class BasketRepository : RepositoryBase<Basket>, IBasketRepository
{
    public BasketRepository(AppDbContext db) : base(db) { }

    public override Task<Basket?> GetByIdAsync(int id, CancellationToken ct = default) =>
        Db.Baskets.Include(b => b.Lines).FirstOrDefaultAsync(b => b.Id == id, ct);

    public Task<Basket?> GetForCustomerAsync(int customerId, CancellationToken ct = default) =>
        Db.Baskets.Include(b => b.Lines).FirstOrDefaultAsync(b => b.CustomerId == customerId, ct);

    public Task<Basket?> GetForGuestAsync(string guestTokenHash, CancellationToken ct = default) =>
        Db.Baskets.Include(b => b.Lines).FirstOrDefaultAsync(b => b.GuestTokenHash == guestTokenHash, ct);

    public async Task<IReadOnlyList<Basket>> ListExpiredAsync(DateTime now, int max, CancellationToken ct = default) =>
        await Db.Baskets.Where(b => b.ExpiresAt <= now).OrderBy(b => b.ExpiresAt).Take(max).ToListAsync(ct);

    // ما تنساه محاولةٌ فشلت بتعارض: السلّتان معاً — سلّة العميل التي دُمج فيها، وسلّة الزائر التي حُذفت —
    // وأسطرهما. بلا نسيان الأسطر تبقى مُعلَّقةً على سلّةٍ مفصولة فتُعاد كتابتها عند الحفظ التالي.
    // النطاق ضيّق عمداً: لا يُمسّ `AuditEntry` ولا شيء خارج السلال (انظر `UserRepository.Reset`).
    public void Reset()
    {
        foreach (var entry in Db.ChangeTracker.Entries()
                     .Where(e => e.Entity is Basket or BasketLine)
                     .ToList())
            entry.State = EntityState.Detached;
    }
}
