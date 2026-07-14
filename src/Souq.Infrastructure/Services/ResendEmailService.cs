using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Souq.Application.Common.Interfaces;

namespace Souq.Infrastructure.Services;

// ApiKey سرّ دوماً (متغيّر بيئة Resend__ApiKey) — لا يُقرأ من appsettings
// المرفوع أبداً (انظر AddInfrastructure). From غير سرّي: يظهر في appsettings
// كقيمة افتراضية قابلة للتعديل (نفس نمط Gmail:Username).
public class ResendOptions
{
    public string ApiKey { get; set; } = "";
    // onboarding@resend.dev يعمل فوراً بلا تحقّق نطاق — مخصّص للتطوير/الاختبار.
    // أي نطاق حقيقي في الإنتاج يجب أن يُتحقَّق أولاً في لوحة Resend.
    public string From { get; set; } = "Marka <onboarding@resend.dev>";
    public string FrontendUrl { get; set; } = "http://localhost:5173";
}

// ============================================================================
// ResendEmailService — التنفيذ الحقيقي عبر Resend API. HttpClient خام فقط
// (بلا SDK/مكتبة إضافية): طلب POST واحد لكل بريد بترويسة Authorization: Bearer.
// حقل ثابت مشترك للـ HttpClient (لا "new HttpClient()" لكل طلب) لتفادي استنزاف
// المقابس؛ ترويسة التفويض تُبنى لكل طلب على حدة (لا على HttpClient نفسه) كي
// تبقى آمنة مع نسخ متزامنة متعددة من الخدمة (IEmailService مسجَّلة Scoped).
// يُفعَّل تلقائياً في DI حين يوجد Resend:ApiKey مضبوطاً؛ وإلا يبقى
// GmailEmailService (أو ConsoleEmailService) كبديل (انظر AddInfrastructure).
// ============================================================================
public class ResendEmailService : IEmailService
{
    private const string Endpoint = "https://api.resend.com/emails";
    private static readonly HttpClient Http = new();

    private readonly ResendOptions _opts;
    private readonly ILogger<ResendEmailService> _logger;

    public ResendEmailService(IOptions<ResendOptions> opts, ILogger<ResendEmailService> logger)
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
            from = _opts.From,
            to = new[] { toEmail },
            subject,
            html = htmlBody,
        });

        // تشخيص الإعداد قبل أي محاولة إرسال — لا يطبع المفتاح نفسه أبداً، فقط
        // "configured"/"missing"، إلى جانب FrontendUrl الفعلي المُستخدَم في بناء
        // رابط إعادة التعيين (لتشخيص شكاوى "لم يصلني البريد"/"الرابط خاطئ").
        _logger.LogInformation(
            "Resend config: ApiKey={ApiKeyStatus} From={From} FrontendUrl={FrontendUrl}",
            string.IsNullOrWhiteSpace(_opts.ApiKey) ? "missing" : "configured",
            _opts.From, _opts.FrontendUrl);

        // سجلّ محاولة صريح قبل الإرسال — نفس منهج GmailEmailService: نعرف من
        // السجل أن الإرسال بدأ فعلاً وإلى أي عنوان بالضبط.
        _logger.LogInformation(
            "إرسال بريد Resend: المستقبِل={Email} الموضوع=\"{Subject}\" المرسِل={From}",
            toEmail, subject, _opts.From);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opts.ApiKey);

            using var response = await Http.SendAsync(request, ct);
            // نقرأ الجسم دوماً (نجاحاً أو فشلاً) — Resend يُعيد { "id": "..." } عند
            // النجاح و{ "message": "...", "name": "..." } عند الفشل؛ كلاهما مفيد للتشخيص.
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "✅ نجح إرسال بريد \"{Subject}\" إلى {Email} عبر Resend — الحالة={StatusCode} الاستجابة={Body}",
                    subject, toEmail, (int)response.StatusCode, responseBody);
            }
            else
            {
                _logger.LogError(
                    "❌ فشل إرسال بريد عبر Resend إلى {Email} (الموضوع=\"{Subject}\"): الحالة={StatusCode} الاستجابة={Body}",
                    toEmail, subject, (int)response.StatusCode, responseBody);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // فشل الإرسال لا يجب أن يُسقط تدفّق العمل (مثلاً: لا نكشف فشل Resend
            // لطالب إعادة التعيين — الرسالة الموحّدة "نجاح دائماً" تبقى كما هي).
            _logger.LogError(ex,
                "❌ فشل إرسال بريد عبر Resend إلى {Email} (الموضوع=\"{Subject}\"): {ErrorType}: {ErrorMessage}",
                toEmail, subject, ex.GetType().Name, ex.Message);
        }
    }
}
