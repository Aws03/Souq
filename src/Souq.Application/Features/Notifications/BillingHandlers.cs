using Souq.Application.Common.Notifications;
using Souq.Application.Common.Security;
using Souq.Application.Common.Tenancy;
using Souq.Application.Features.Billing;
using Souq.Domain.Common;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Notifications;

// ============================================================================
// تذكيرُ التاجر بفاتورة اشتراكٍ استحقّت (C6، [ADR-0058](0058)).
//
// **يقرأ الحقيقة وقت الإرسال، لا وقت الجدولة.** الحمولةُ معرّفٌ فقط، فتذكيرٌ انتظر في الصندوق
// ساعةً بعد سدادٍ وصل لا يُرسل مبلغاً لم يعد مستحقّاً — يخرج صامتاً. وهو ما يجعل إعادةَ المحاولة
// (التسليم «مرّة على الأقل») آمنةً بلا حالةٍ خاصّة.
//
// **ويصل بطريقين**: إشعارٌ في اللوحة لمن يملك إعدادات المتجر، وبريدٌ إليهم. والبريدُ ليس ترفاً
// هنا: مَن لا يدفع غالباً لا يفتح لوحته، وتذكيرٌ لا يصل إلّا لمن يدخل ليقرأه ليس تذكيراً.
//
// **ولا رابطَ دفعٍ في أيٍّ منهما**: التحصيلُ حوالةٌ بقرار `C-15`، وزرُّ «ادفع الآن» يَعِد ببوّابةٍ
// لا وجود لها — وهي القاعدة نفسها التي تحكم شاشةَ اشتراك التاجر.
// ============================================================================
public sealed class InvoiceOverdueReminderHandler : INotificationMessageHandler<InvoiceOverdueReminder>
{
    private readonly IPlatformInvoiceRepository _invoices;
    private readonly IUserRepository _users;
    private readonly INotificationRepository _notifications;
    private readonly IStoreOrigins _origins;
    private readonly ITenantContext _tenant;
    private readonly NotificationEmails _emails;
    private readonly TimeProvider _clock;
    private readonly IUnitOfWork _uow;

    public InvoiceOverdueReminderHandler(
        IPlatformInvoiceRepository invoices, IUserRepository users, INotificationRepository notifications,
        IStoreOrigins origins, ITenantContext tenant, NotificationEmails emails, TimeProvider clock, IUnitOfWork uow)
    {
        _invoices = invoices; _users = users; _notifications = notifications;
        _origins = origins; _tenant = tenant; _emails = emails; _clock = clock; _uow = uow;
    }

    public async Task HandleAsync(InvoiceOverdueReminder reminder, CancellationToken ct)
    {
        var store = _tenant.RequireTenant();

        // شرطُ المتجر مع المعرّف: الفاتورةُ جدولُ منصّةٍ بلا مرشّح، فعزلُها شرطٌ يُكتب بيد —
        // ورسالةٌ في الصندوق تحمل معرّفاً هي بالضبط المدخلُ الذي لا يجوز الوثوقُ به وحده.
        var invoice = await _invoices.GetForTenantAsync(reminder.InvoiceId, store.Id, ct);
        var now = _clock.GetUtcNow().UtcDateTime;

        // سُدّدت أو أُلغيت أو لم تعد متأخّرة بين الجدولة والإرسال ⇒ لا تذكير. الخروجُ صامتٌ
        // ونجاح: الرسالةُ أُنجزت، ولا شيء يُطالَب به.
        if (invoice is null || !invoice.IsOverdueAt(now)) return;

        var data = NotificationData.Of(
            ("invoiceId", invoice.Id),
            ("invoiceNumber", NotificationData.Short(invoice.Number ?? "")),
            ("outstanding", invoice.Outstanding.ToString()),
            ("daysOverdue", invoice.DaysOverdueAt(now)));

        var recipients = await _users.ListActiveIdsByRolesAsync(
            RolePermissions.RolesGranting(Permissions.Store.Settings), ct);

        foreach (var userId in recipients)
            await _notifications.AddAsync(new Notification(userId, NotificationKinds.InvoiceOverdue, data), ct);

        await _uow.SaveChangesAsync(ct);

        // البريدُ بعد الحفظ وخارج أي معاملة (ADR-0021): مزوّدٌ بطيء لا يحبس صفّاً.
        var origin = await _origins.ForStoreAsync(store.Id, ct);
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["invoiceNumber"] = invoice.Number ?? "",
            ["outstanding"] = invoice.Outstanding.ToString(),
            ["daysOverdue"] = invoice.DaysOverdueAt(now).ToString(),
        };

        foreach (var email in await _users.ListActiveEmailsByRolesAsync(
                     RolePermissions.RolesGranting(Permissions.Store.Settings), ct))
        {
            await _emails.SendAsync(
                email, EmailTemplate.InvoiceOverdue, origin, $"{origin.TrimEnd('/')}/admin/subscription", values, ct);
        }
    }
}
