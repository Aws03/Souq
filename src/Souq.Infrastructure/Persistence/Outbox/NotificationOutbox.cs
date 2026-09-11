using Souq.Application.Common.Notifications;
using Souq.Application.Common.Tenancy;

namespace Souq.Infrastructure.Persistence.Outbox;

// INotificationOutbox: يضيف الصفّ لوحدة العمل الحالية فيُحفظ مع SaveChanges التالي — مع التغيير الذي سبّبه أو لا يُحفظ أبداً.
// المتجر من نطاق الطلب (المنصّة ⇒ null)، والمُرسِل يعالج الرسالة داخل النطاق نفسه.
internal sealed class NotificationOutbox : INotificationOutbox
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly TimeProvider _clock;

    public NotificationOutbox(AppDbContext db, ITenantContext tenant, TimeProvider clock)
    {
        _db = db; _tenant = tenant; _clock = clock;
    }

    public void Enqueue(object message) =>
        _db.OutboxMessages.Add(OutboxMessage.For(
            message, _tenant.Scope == TenantScope.Tenant ? _tenant.Tenant!.Id : null, _clock.GetUtcNow().UtcDateTime));
}
