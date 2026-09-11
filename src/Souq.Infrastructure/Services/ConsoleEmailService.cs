using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Souq.Application.Common.Notifications;

namespace Souq.Infrastructure.Services;

public class ConsoleEmailOptions
{
    // true في Development فقط: لا بريد حقيقي هناك، والمطوّر يحتاج الرابط لاختبار التدفّق.
    public bool IncludeLinksInLog { get; set; }
}

// ============================================================================
// ConsoleEmailService — "بريد" في السجل بدل مزوّد: في التطوير والاختبار تلقائياً، وخارجهما بإذن صريح فقط
// (Email:Provider=Log — المرحلة 14: لا بديل طرفي صامت في الإنتاج؛ بلا مزوّد ولا إذن يرفض الـ API الإقلاع). قبل Phase 1A كان
// يطبع رابط إعادة التعيين كاملاً في كل البيئات (Phase 0 B2): الآن الرابط يظهر في Development وحده، وخارجه تحذير "لم يُرسَل"
// بنوع الرسالة ومستلم مُقنَّع — فيلاحظ المشغّل أن البريد غير مضبوط دون تسريب أي سرّ.
// ============================================================================
public class ConsoleEmailService : IEmailSender
{
    private readonly ConsoleEmailOptions _opts;
    private readonly ILogger<ConsoleEmailService> _logger;

    public ConsoleEmailService(IOptions<ConsoleEmailOptions> opts, ILogger<ConsoleEmailService> logger)
    {
        _opts = opts.Value; _logger = logger;
    }

    public Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        var recipient = LogRedaction.MaskEmail(message.To);
        if (_opts.IncludeLinksInLog)
            _logger.LogInformation("📧 [development only] {Kind} for {Recipient}: {Link}", message.Kind, recipient, message.ActionUrl ?? "(no link)");
        else
            _logger.LogWarning("No email provider configured — {Kind} to {Recipient} was not sent", message.Kind, recipient);
        return Task.CompletedTask;
    }
}
