using Souq.Domain.Entities;

namespace Souq.Domain.Interfaces;

// منفذ الكتابة للإشعارات داخل التطبيق (المرحلة 14). القائمة وعدد غير المقروء عبر INotificationQueries (ADR-0008).
public interface INotificationRepository : IRepository<Notification>
{
    // إشعار لحساب بعينه في متجر السياق — إشعار غيره كأنه غير موجود.
    Task<Notification?> FindForRecipientAsync(int id, int recipientUserId, CancellationToken ct = default);

    // تعليم كل غير المقروء بتحديث واحد (لا تحميل مئات الصفوف).
    Task<int> MarkAllReadAsync(int recipientUserId, DateTime now, CancellationToken ct = default);
}
