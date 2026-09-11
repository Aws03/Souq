using Souq.Domain.Common;
using Souq.Domain.Exceptions;

namespace Souq.Domain.Entities;

// ============================================================================
// StorePaymentAccount — حساب البوّابة الخاص بمتجر (المرحلة 11، D-13): حين يوجد يُقبض مال المتجر في حسابه هو، وإلا فحساب
// النشر الافتراضي. المفتاح العلني يُحفظ كما هو (يُرسَل للمتصفّح أصلاً)، والسرّان مشفَّران فقط — الكيان لا يرى نصّهما
// أبداً: التطبيق يتحقّق من صيغتهما (PaymentKeyRules) ثم يشفّرهما مربوطَين بهذا المتجر ويسلّم الكيان النصّ المشفّر وتلميحاً.
// ============================================================================
public class StorePaymentAccount : Entity, ITenantOwned
{
    public const string Stripe = "stripe";
    public const int KeyMaxLength = 255;
    public const int CipherMaxLength = 1024;

    public int TenantId { get; private set; }
    public string Provider { get; private set; } = Stripe;
    public string PublishableKey { get; private set; } = default!;
    public bool LiveMode { get; private set; }
    public string SecretKeyCipher { get; private set; } = default!;
    public string SecretKeyHint { get; private set; } = default!;
    public string? WebhookSecretCipher { get; private set; }
    public int? UpdatedByUserId { get; private set; }

    private StorePaymentAccount() { }

    // أول ضبط: السرّ إلزامي. سرّ الإشعارات اختياري (بدونه يعتمد التأكيد على المتصفّح ومنسّق المهلة).
    public StorePaymentAccount(string publishableKey, string secretKeyCipher, string secretKeyHint, bool liveMode,
                               string? webhookSecretCipher, int? updatedByUserId)
    {
        if (string.IsNullOrWhiteSpace(secretKeyCipher))
            throw new InvalidPaymentOperationException("المفتاح السرّي مطلوب عند ربط حساب المتجر", "InvalidPaymentKeys");
        Update(publishableKey, liveMode, secretKeyCipher, secretKeyHint, webhookSecretCipher, updatedByUserId);
    }

    // تحديث: سرّ null يُبقي المحفوظ. الوضع (تجريبي/حقيقي) يلزم أن يطابق المفتاح العلني — والتطبيق طابقه بالسرّي قبل
    // التشفير؛ فلا يخلط متجرٌ مفتاحاً تجريبياً بآخر حقيقي.
    public void Update(string publishableKey, bool liveMode, string? secretKeyCipher, string? secretKeyHint,
                       string? webhookSecretCipher, int? updatedByUserId)
    {
        if (PaymentKeyRules.PublishableMode(publishableKey) is not { } mode || mode != liveMode)
            throw new InvalidPaymentOperationException("المفتاح العلني بصيغة Stripe (pk_test_ أو pk_live_) وبوضع المفتاح السرّي نفسه",
                "InvalidPaymentKeys");
        if (secretKeyCipher is not null && (secretKeyCipher.Length > CipherMaxLength || string.IsNullOrWhiteSpace(secretKeyHint)))
            throw new InvalidPaymentOperationException("المفتاح السرّي غير صالح", "InvalidPaymentKeys");
        if (secretKeyCipher is null && liveMode != LiveMode && SecretKeyCipher is not null)
            throw new InvalidPaymentOperationException("تغيير الوضع (تجريبي/حقيقي) يتطلّب مفتاحاً سرّياً جديداً", "InvalidPaymentKeys");
        if (webhookSecretCipher is { Length: > CipherMaxLength })
            throw new InvalidPaymentOperationException("سرّ الإشعارات غير صالح", "InvalidPaymentKeys");

        PublishableKey = publishableKey.Trim();
        LiveMode = liveMode;
        if (secretKeyCipher is not null)
        {
            SecretKeyCipher = secretKeyCipher;
            SecretKeyHint = secretKeyHint!;
        }
        if (webhookSecretCipher is not null) WebhookSecretCipher = webhookSecretCipher;
        UpdatedByUserId = updatedByUserId;
    }
}

// صيغ مفاتيح Stripe المعلنة: علني pk_test_/pk_live_، سرّي sk_ أو مقيَّد rk_ بالوضعين، وسرّ الإشعارات whsec_.
// null ⇒ ليس مفتاحاً من هذا النوع.
public static class PaymentKeyRules
{
    public static bool? PublishableMode(string? key) => Mode(key, "pk_");

    public static bool? SecretMode(string? key) => Mode(key, "sk_") ?? Mode(key, "rk_");

    public static bool IsWebhookSecret(string? key) => key?.Trim() is { Length: > 10 } k && k.StartsWith("whsec_", StringComparison.Ordinal);

    // تلميح يعرّف المفتاح في الواجهة بلا كشفه: آخر أربعة محارف فقط.
    public static string Hint(string secretKey) => $"…{secretKey.Trim()[^4..]}";

    private static bool? Mode(string? key, string prefix)
    {
        var k = key?.Trim();
        if (k is null || k.Length > StorePaymentAccount.KeyMaxLength || k.Any(char.IsWhiteSpace)) return null;
        if (k.StartsWith($"{prefix}live_", StringComparison.Ordinal) && k.Length > prefix.Length + 9) return true;
        if (k.StartsWith($"{prefix}test_", StringComparison.Ordinal) && k.Length > prefix.Length + 9) return false;
        return null;
    }
}
