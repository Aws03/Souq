using Microsoft.Extensions.Options;

namespace Souq.Infrastructure.Services;

// إعدادات Stripe. SecretKey وWebhookSecret سرّان حقيقيان (user-secrets/متغيرات
// بيئة فقط). PublishableKey ليس سرّاً — يُصمَّم لإرساله للواجهة (Stripe.js يحتاجه).
public class StripeSettings
{
    public string SecretKey { get; set; } = "";
    public string PublishableKey { get; set; } = "";
    public string WebhookSecret { get; set; } = "";
}

// يُسجَّل فقط حين تعمل بوّابة Stripe. خارج Development بلا مفتاح علني تظنّ الواجهة أن البوّابة
// تجريبية فتعرض زرّ إتمام مباشر، والخادم يرى نيّة دفع غير مكتملة فيُلغي الطلب — دفع معطّل بصمت.
public sealed class StripeSettingsValidator : IValidateOptions<StripeSettings>
{
    private readonly bool _requirePublishableKey;
    public StripeSettingsValidator(bool requirePublishableKey) => _requirePublishableKey = requirePublishableKey;

    public ValidateOptionsResult Validate(string? name, StripeSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.SecretKey))
            return ValidateOptionsResult.Fail("Stripe:SecretKey مطلوب حين تعمل بوّابة Stripe.");
        if (_requirePublishableKey && string.IsNullOrWhiteSpace(settings.PublishableKey))
            return ValidateOptionsResult.Fail(
                "Stripe:PublishableKey مطلوب خارج Development — بدونه لا تعرض الواجهة نموذج البطاقة.");
        return ValidateOptionsResult.Success;
    }
}
