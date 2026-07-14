using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Souq.Application.Common.Interfaces;

namespace Souq.Infrastructure.Services;

// ApiKey سرّ دوماً (متغيّر بيئة Brevo__ApiKey) — لا يُقرأ من appsettings
// المرفوع أبداً (انظر AddInfrastructure). SenderName/SenderEmail غير سرّيين:
// يظهران في appsettings كقيمة افتراضية قابلة للتعديل (نفس نمط Resend:From).
public class BrevoOptions
{
    public string ApiKey { get; set; } = "";
    public string SenderName { get; set; } = "Marka";
    public string SenderEmail { get; set; } = "aws.03.dev@gmail.com";
    public string FrontendUrl { get; set; } = "http://localhost:5173";
}

// ============================================================================
// BrevoEmailService — بديل ثالث عبر Brevo API (Sendinblue سابقاً). HttpClient
// خام فقط (بلا SDK/مكتبة إضافية)؛ المصادقة هنا برأس "api-key" مباشرة، لا
// "Authorization: Bearer" كما في Resend — فرق أساسي بين المزوّدَين. نفس حقل
// HttpClient الثابت المشترك ونفس منهج التسجيل التفصيلي في ResendEmailService.
// يُفعَّل حين لا يوجد Resend:ApiKey لكن يوجد Brevo:ApiKey مضبوطاً؛ وإلا يبقى
// GmailEmailService (أو ConsoleEmailService) كبديل (انظر AddInfrastructure).
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
    {
        var subject = $"تأكيد الطلب #{orderId} — ماركة";
        return SendAsync(toEmail, subject, EmailTemplates.OrderConfirmation(orderId), ct);
    }

    public Task SendPasswordResetEmailAsync(string toEmail, string resetToken, CancellationToken ct = default)
    {
        var link = $"{_opts.FrontendUrl.TrimEnd('/')}/reset-password?token={resetToken}";
        return SendAsync(toEmail, "إعادة تعيين كلمة المرور — ماركة", EmailTemplates.PasswordReset(link), ct);
    }

    private async Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new
        {
            sender = new { name = _opts.SenderName, email = _opts.SenderEmail },
            to = new[] { new { email = toEmail } },
            subject,
            htmlContent = htmlBody,
        });

        // تشخيص الإعداد قبل أي محاولة إرسال — لا يطبع المفتاح نفسه أبداً، فقط
        // "configured"/"missing"، إلى جانب FrontendUrl الفعلي المُستخدَم في بناء
        // رابط إعادة التعيين (نفس منهج ResendEmailService).
        _logger.LogInformation(
            "Brevo config: ApiKey={ApiKeyStatus} Sender={SenderName} <{SenderEmail}> FrontendUrl={FrontendUrl}",
            string.IsNullOrWhiteSpace(_opts.ApiKey) ? "missing" : "configured",
            _opts.SenderName, _opts.SenderEmail, _opts.FrontendUrl);

        _logger.LogInformation(
            "إرسال بريد Brevo: المستقبِل={Email} الموضوع=\"{Subject}\" المرسِل={SenderName} <{SenderEmail}>",
            toEmail, subject, _opts.SenderName, _opts.SenderEmail);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };
            // Brevo يتوقّع المفتاح في رأس "api-key" مباشرة، لا "Authorization: Bearer".
            request.Headers.Add("api-key", _opts.ApiKey);

            using var response = await Http.SendAsync(request, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "✅ نجح إرسال بريد \"{Subject}\" إلى {Email} عبر Brevo — الحالة={StatusCode} الاستجابة={Body}",
                    subject, toEmail, (int)response.StatusCode, responseBody);
            }
            else
            {
                _logger.LogError(
                    "❌ فشل إرسال بريد عبر Brevo إلى {Email} (الموضوع=\"{Subject}\"): الحالة={StatusCode} الاستجابة={Body}",
                    toEmail, subject, (int)response.StatusCode, responseBody);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // فشل الإرسال لا يجب أن يُسقط تدفّق العمل (مثلاً: لا نكشف فشل Brevo
            // لطالب إعادة التعيين — الرسالة الموحّدة "نجاح دائماً" تبقى كما هي).
            _logger.LogError(ex,
                "❌ فشل إرسال بريد عبر Brevo إلى {Email} (الموضوع=\"{Subject}\"): {ErrorType}: {ErrorMessage}",
                toEmail, subject, ex.GetType().Name, ex.Message);
        }
    }
}
