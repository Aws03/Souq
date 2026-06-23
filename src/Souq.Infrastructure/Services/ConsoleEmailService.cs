using Microsoft.Extensions.Logging;
using Souq.Application.Common.Interfaces;

namespace Souq.Infrastructure.Services;

// تنفيذ تجريبي للبريد: يطبع في السجل بدل الإرسال الفعلي. يُستبدل لاحقاً بـ SMTP.
public class ConsoleEmailService : IEmailService
{
    private readonly ILogger<ConsoleEmailService> _logger;
    public ConsoleEmailService(ILogger<ConsoleEmailService> logger) => _logger = logger;

    public Task SendOrderConfirmationAsync(string toEmail, int orderId, CancellationToken ct = default)
    {
        _logger.LogInformation("📧 تأكيد الطلب #{OrderId} أُرسل إلى {Email}", orderId, toEmail);
        return Task.CompletedTask;
    }
}
