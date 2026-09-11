using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Souq.Application.Common.Interfaces;

namespace Souq.Infrastructure.Services;

// ApiKey سرّ دوماً (Brevo__ApiKey). SenderEmail يأتي من الإعداد (Brevo:SenderEmail أو
// عنوان المرسِل المشترك Gmail:Username) — لا عنوان شخصي مكتوب في الكود (Phase 0 B12).
public class BrevoOptions
{
    public string ApiKey { get; set; } = "";
    public string SenderName { get; set; } = "Marka";
    public string SenderEmail { get; set; } = "";
    public string FrontendUrl { get; set; } = "http://localhost:5173";
}

// ============================================================================
// BrevoEmailService — بديل عبر Brevo API. المصادقة برأس "api-key" (لا Bearer). نفس
// سياسة السجل المنقَّح في ResendEmailService.
// ============================================================================
public class BrevoEmailService : IEmailService
{
    private const string Endpoint = "https://api.brevo.com/v3/smtp/email";
    private static readonly HttpClient Http = new();

    private readonly BrevoOptions _opts;
    private readonly ILogger<BrevoEmailService> _logger;

    public BrevoEmailService(IOptions<BrevoOptions> opts, ILogger<BrevoEmailService> logger)
    {
        _opts = opts.Value; _logger = logger;
    }

    public Task SendOrderConfirmationAsync(string toEmail, int orderId, CancellationToken ct = default)
        => SendAsync(toEmail, $"تأكيد الطلب #{orderId} — ماركة", EmailTemplates.OrderConfirmation(orderId), ct);

    public Task SendPasswordResetEmailAsync(string toEmail, string resetToken, CancellationToken ct = default)
    {
        var link = $"{_opts.FrontendUrl.TrimEnd('/')}/reset-password?token={resetToken}";
        return SendAsync(toEmail, "إعادة تعيين كلمة المرور — ماركة", EmailTemplates.PasswordReset(link), ct);
    }

    private async Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct)
    {
        var recipient = LogRedaction.MaskEmail(toEmail);
        if (string.IsNullOrWhiteSpace(_opts.SenderEmail))
        {
            _logger.LogError("Brevo مضبوط بلا عنوان مرسِل (Brevo:SenderEmail) — لم يُرسَل \"{Subject}\" إلى {Recipient}",
                subject, recipient);
            return;
        }

        var payload = JsonSerializer.Serialize(new
        {
            sender = new { name = _opts.SenderName, email = _opts.SenderEmail },
            to = new[] { new { email = toEmail } },
            subject,
            htmlContent = htmlBody,
        });

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("api-key", _opts.ApiKey);

            using var response = await Http.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("أُرسل بريد \"{Subject}\" إلى {Recipient} عبر Brevo (الحالة {StatusCode})",
                    subject, recipient, (int)response.StatusCode);
                return;
            }

            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("فشل إرسال بريد \"{Subject}\" إلى {Recipient} عبر Brevo: الحالة {StatusCode} — {ProviderError}",
                subject, recipient, (int)response.StatusCode, LogRedaction.Truncate(errorBody));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "فشل إرسال بريد \"{Subject}\" إلى {Recipient} عبر Brevo", subject, recipient);
        }
    }
}
