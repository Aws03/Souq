using Microsoft.EntityFrameworkCore;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;

namespace Souq.Infrastructure.Persistence.Repositories;

// إشعارات حسابات متجر السياق (المرحلة 14) — مرشّح المستأجر يحصر القراءة والتحديث الجماعي في المتجر.
public class NotificationRepository : RepositoryBase<Notification>, INotificationRepository
{
    public NotificationRepository(AppDbContext db) : base(db) { }

    public Task<Notification?> FindForRecipientAsync(int id, int recipientUserId, CancellationToken ct = default) =>
        Db.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.RecipientUserId == recipientUserId, ct);

    public Task<int> MarkAllReadAsync(int recipientUserId, DateTime now, CancellationToken ct = default) =>
        Db.Notifications.Where(n => n.RecipientUserId == recipientUserId && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), ct);
}
