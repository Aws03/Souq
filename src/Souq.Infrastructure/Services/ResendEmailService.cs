using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Souq.Application.Common.Notifications;

namespace Souq.Infrastructure.Services;

// ApiKey سرّ دوماً (متغيّر بيئة Resend__ApiKey/user-secrets). From غير سرّي: عنوان النشر المُتحقَّق منه لدى Resend — اسم
// المرسِل يُستبدل باسم المتجر لكل رسالة (المرحلة 14).
public class ResendOptions
{
    public string ApiKey { get; set; } = "";
    // onboarding@resend.dev يعمل فوراً بلا تحقّق نطاق — مخصّص للتطوير/الاختبار.
    public string From { get; set; } = "Souq <onboarding@resend.dev>";
}

// ============================================================================
// ResendEmailService — إرسال حقيقي عبر Resend API. عميل HTTP من IHttpClientFactory (مهلة 15 ثانية، اتصالات مُدارة تحترم تغيّر
// DNS)؛ ترويسة التفويض لكل طلب على حدة. الفشل يرمي EmailDeliveryException فيعيد صندوق الصادر المحاولة. السجل: نوع الرسالة
// والمستلم مُقنَّعاً، لا جسم عند النجاح، وجسم مُختصَر منقَّح عند الفشل فقط (Security.md §9).
// ============================================================================
public class ResendEmailService : IEmailSender
{
    private const string Endpoint = "https://api.resend.com/emails";

    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private readonly HttpClient _http;
    private readonly ResendOptions _opts;
    private readonly ILogger<ResendEmailService> _logger;

    public ResendEmailService(HttpClient http, IOptions<ResendOptions> opts, ILogger<ResendEmailService> logger)
    {
        _http = http; _opts = opts.Value; _logger = logger;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        var recipient = LogRedaction.MaskEmail(message.To);
        var payload = JsonSerializer.Serialize(new
        {
            from = EmailSenders.WithDisplayName(_opts.From, message.FromName),
            to = new[] { message.To },
            reply_to = message.ReplyTo is null ? null : new[] { message.ReplyTo },
            subject = message.Subject,
            html = message.HtmlBody,
            text = message.TextBody,
        }, Json);

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opts.ApiKey);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new EmailDeliveryException("تعذّر الاتصال بـ Resend", ex);
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Email {Kind} sent to {Recipient} via Resend ({StatusCode})",
                    message.Kind, recipient, (int)response.StatusCode);
                return;
            }

            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Resend rejected email {Kind} to {Recipient}: {StatusCode} — {ProviderError}",
                message.Kind, recipient, (int)response.StatusCode, LogRedaction.Truncate(LogRedaction.MaskEmails(errorBody)));
            throw new EmailDeliveryException($"Resend أعاد الحالة {(int)response.StatusCode}");
        }
    }
}
