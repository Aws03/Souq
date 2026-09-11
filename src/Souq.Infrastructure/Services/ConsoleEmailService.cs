using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Souq.Application.Common.Interfaces;

namespace Souq.Infrastructure.Services;

public class ConsoleEmailOptions
{
    // true في Development فقط: لا بريد حقيقي هناك، والمطوّر يحتاج الرابط لاختبار التدفّق.
    public bool IncludeLinksInLog { get; set; }
    public string FrontendUrl { get; set; } = "http://localhost:5173";
}

// ============================================================================
// ConsoleEmailService — بديل حين لا يوجد أي مزوّد بريد مضبوط. قبل Phase 1A كان يطبع
// رابط إعادة التعيين كاملاً في السجل في كل البيئات: من يقرأ السجل يستولي على أي حساب
// (Phase 0 B2). الآن: الرابط يظهر في Development فقط؛ خارجها تحذير "لم يُرسَل" بعنوان
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

    public Task SendPasswordResetEmailAsync(string toEmail, string resetToken, CancellationToken ct = default)
    {
        if (_opts.IncludeLinksInLog)
        {
            var link = $"{_opts.FrontendUrl.TrimEnd('/')}/reset-password?token={resetToken}";
            _logger.LogInformation("📧 [تطوير فقط] رابط إعادة التعيين لـ {Recipient}: {Link}",
                LogRedaction.MaskEmail(toEmail), link);
        }
        else
        {
            _logger.LogWarning("لا مزوّد بريد مضبوط — لم يُرسَل بريد إعادة تعيين كلمة المرور إلى {Recipient}",
                LogRedaction.MaskEmail(toEmail));
        }
        return Task.CompletedTask;
    }
}
