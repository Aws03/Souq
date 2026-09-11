using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Souq.Application.Common.Interfaces;

namespace Souq.Infrastructure.Services;

public class ConsoleEmailOptions
{
    // true في Development فقط: لا بريد حقيقي هناك، والمطوّر يحتاج الرابط لاختبار التدفّق.
    public bool IncludeLinksInLog { get; set; }
}

// ============================================================================
// ConsoleEmailService — بديل حين لا يوجد أي مزوّد بريد مضبوط. قبل Phase 1A كان يطبع
// رابط إعادة التعيين كاملاً في السجل في كل البيئات: من يقرأ السجل يستولي على أي حساب
// (Phase 0 B2). الآن: الروابط تظهر في Development فقط؛ خارجها تحذير "لم يُرسَل" بعنوان
// مُقنَّع — فيلاحظ المشغّل أن البريد غير مضبوط دون تسريب أي سرّ.
// ============================================================================
public class ConsoleEmailService : IEmailService
{
    private readonly ConsoleEmailOptions _opts;
    private readonly ILogger<ConsoleEmailService> _logger;

    public ConsoleEmailService(IOptions<ConsoleEmailOptions> opts, ILogger<ConsoleEmailService> logger)
    {
        _opts = opts.Value; _logger = logger;
    }

    public Task SendOrderConfirmationAsync(string toEmail, int orderId, CancellationToken ct = default)
    {
        if (_opts.IncludeLinksInLog)
            _logger.LogInformation("📧 [تطوير] تأكيد الطلب #{OrderId} إلى {Recipient}", orderId, LogRedaction.MaskEmail(toEmail));
        else
            _logger.LogWarning("لا مزوّد بريد مضبوط — لم يُرسَل تأكيد الطلب #{OrderId} إلى {Recipient}",
                orderId, LogRedaction.MaskEmail(toEmail));
        return Task.CompletedTask;
    }

    public Task SendPasswordResetEmailAsync(string toEmail, string resetLink, CancellationToken ct = default) =>
        LogLink("إعادة تعيين كلمة المرور", toEmail, resetLink);

    public Task SendEmailVerificationAsync(string toEmail, string verificationLink, CancellationToken ct = default) =>
        LogLink("تأكيد البريد", toEmail, verificationLink);

    public Task SendInvitationAsync(string toEmail, string storeName, string invitationLink, CancellationToken ct = default) =>
        LogLink("دعوة حساب", toEmail, invitationLink);

    private Task LogLink(string purpose, string toEmail, string link)
    {
        if (_opts.IncludeLinksInLog)
            _logger.LogInformation("📧 [تطوير فقط] رابط {Purpose} لـ {Recipient}: {Link}",
                purpose, LogRedaction.MaskEmail(toEmail), link);
        else
            _logger.LogWarning("لا مزوّد بريد مضبوط — لم يُرسَل بريد {Purpose} إلى {Recipient}",
                purpose, LogRedaction.MaskEmail(toEmail));
        return Task.CompletedTask;
    }
}
