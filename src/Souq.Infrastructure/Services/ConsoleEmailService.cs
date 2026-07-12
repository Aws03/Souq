using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Souq.Application.Common.Interfaces;

namespace Souq.Infrastructure.Services;

// تنفيذ تجريبي للبريد: يطبع في السجل بدل الإرسال الفعلي. يعمل تلقائياً حين
// لا يوجد Gmail:AppPassword مضبوط (تطوير محلي بلا حساب Gmail حقيقي) — نفس نمط
// FakePaymentService. يطبع الرابط الكامل فعلياً كي يمكن اختبار التدفّق يدوياً
// من السجل دون بريد حقيقي.
public class ConsoleEmailService : IEmailService
{
    private readonly ILogger<ConsoleEmailService> _logger;
    private readonly IConfiguration _config;

    public ConsoleEmailService(ILogger<ConsoleEmailService> logger, IConfiguration config)
    {
        _logger = logger; _config = config;
    }

    public Task SendOrderConfirmationAsync(string toEmail, int orderId, CancellationToken ct = default)
    {
        _logger.LogInformation("📧 تأكيد الطلب #{OrderId} أُرسل إلى {Email}", orderId, toEmail);
        return Task.CompletedTask;
    }

    public Task SendPasswordResetEmailAsync(string toEmail, string resetToken, CancellationToken ct = default)
    {
        var frontendUrl = _config["App:FrontendUrl"] ?? "http://localhost:5173";
        var link = $"{frontendUrl.TrimEnd('/')}/reset-password?token={resetToken}";
        _logger.LogInformation("📧 رابط إعادة تعيين كلمة المرور لـ {Email}: {Link}", toEmail, link);
        return Task.CompletedTask;
    }
}
