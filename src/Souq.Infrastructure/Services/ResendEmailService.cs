using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Souq.Application.Common.Interfaces;

namespace Souq.Infrastructure.Services;

// ApiKey سرّ دوماً (متغيّر بيئة Resend__ApiKey/user-secrets). From غير سرّي: يظهر في
// appsettings كقيمة افتراضية قابلة للتعديل.
public class ResendOptions
{
    public string ApiKey { get; set; } = "";
    // onboarding@resend.dev يعمل فوراً بلا تحقّق نطاق — مخصّص للتطوير/الاختبار.
    public string From { get; set; } = "Marka <onboarding@resend.dev>";
    public string FrontendUrl { get; set; } = "http://localhost:5173";
}

// ============================================================================
// ResendEmailService — إرسال حقيقي عبر Resend API. عميل HTTP من IHttpClientFactory (مهلة
// 15 ثانية، اتصالات مُدارة تحترم تغيّر DNS)؛ ترويسة التفويض لكل طلب على حدة. السجل: المستلم
// مُقنَّع، لا جسم استجابة عند النجاح، وجسم مُختصَر عند الفشل فقط (Security.md §9).
// ============================================================================
public class ResendEmailService : IEmailService
{
    private const string Endpoint = "https://api.resend.com/emails";

    private readonly HttpClient _http;
    private readonly ResendOptions _opts;
    private readonly ILogger<ResendEmailService> _logger;

    public ResendEmailService(HttpClient http, IOptions<ResendOptions> opts, ILogger<ResendEmailService> logger)
    {
        _http = http; _opts = opts.Value; _logger = logger;
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
        var payload = JsonSerializer.Serialize(new { from = _opts.From, to = new[] { toEmail }, subject, html = htmlBody });

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opts.ApiKey);

            using var response = await _http.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("أُرسل بريد \"{Subject}\" إلى {Recipient} عبر Resend (الحالة {StatusCode})",
                    subject, recipient, (int)response.StatusCode);
                return;
            }

            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("فشل إرسال بريد \"{Subject}\" إلى {Recipient} عبر Resend: الحالة {StatusCode} — {ProviderError}",
                subject, recipient, (int)response.StatusCode, LogRedaction.Truncate(errorBody));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // فشل الإرسال لا يُسقط تدفّق العمل (لا نكشف لطالب إعادة التعيين شيئاً).
            _logger.LogError(ex, "فشل إرسال بريد \"{Subject}\" إلى {Recipient} عبر Resend", subject, recipient);
        }
    }
}
