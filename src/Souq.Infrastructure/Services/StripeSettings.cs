namespace Souq.Infrastructure.Services;

// إعدادات Stripe. SecretKey وWebhookSecret سرّان حقيقيان (user-secrets/متغيرات
// بيئة فقط). PublishableKey ليس سرّاً — يُصمَّم لإرساله للواجهة (Stripe.js يحتاجه).
public class StripeSettings
{
    public string SecretKey { get; set; } = "";
    public string PublishableKey { get; set; } = "";
    public string WebhookSecret { get; set; } = "";
}
