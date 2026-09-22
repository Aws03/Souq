namespace Souq.Infrastructure.Services;

// المزوّدون الذين يعرفهم هذا المستودع. `Demo` هو محوّلُ العرض المحلّي — لا مال ولا شبكة
// (ADR-0063)، و`Stripe` هو الشكلُ الذي يبيّن أين يدخل مزوّدٌ حقيقي. لا حسابَ ولا عقدَ لأيٍّ
// منهما في هذا المستودع.
public enum PaymentProvider { Demo, Stripe }

// ============================================================================
// اختيار بوّابة الدفع عند الإقلاع (ADR-0020). البوّابة التجريبية تؤكّد كل دفع بلا مال؛ كانت
// تُختار تلقائياً حين يغيب مفتاح Stripe — بما فيها حزمة Docker التي تعمل بـ Production:
// أي زائر يطلب ويستلم طلباً "مدفوعاً" بلا دفع. وسيلة تطوير صارت ثغرة إنتاج بصمت.
// الآن: Development/Testing فقط تختارها ضمنياً. غيرهما يرفض الإقلاع برسالة واضحة ما لم تُطلب
// صراحةً (Payments:Provider=Demo — عرض توضيحي، مع تحذير في السجل عند كل إقلاع).
// ============================================================================
public static class PaymentProviderSelector
{
    public const string ConfigKey = "Payments:Provider";

    public static PaymentProvider Select(string? configuredProvider, string? stripeSecretKey, string environmentName)
    {
        var hasStripeKey = !string.IsNullOrWhiteSpace(stripeSecretKey);
        var configured = configuredProvider?.Trim();

        if (string.IsNullOrEmpty(configured))
        {
            if (hasStripeKey) return PaymentProvider.Stripe;
            if (IsLocal(environmentName)) return PaymentProvider.Demo;
            throw new InvalidOperationException(
                $"لا بوّابة دفع مضبوطة في بيئة {environmentName}: اضبط Stripe:SecretKey، " +
                $"أو {ConfigKey}=Demo صراحةً لعرض توضيحي بلا دفع حقيقي.");
        }

        if (configured.Equals(nameof(PaymentProvider.Stripe), StringComparison.OrdinalIgnoreCase))
            return hasStripeKey
                ? PaymentProvider.Stripe
                : throw new InvalidOperationException($"{ConfigKey}=Stripe لكن Stripe:SecretKey غير مضبوط.");

        // `Fake` مقبولٌ اسماً قديماً: إعداداتٌ مكتوبة قبل إعادة التسمية لا تُسقط إقلاعاً.
        if (configured.Equals(nameof(PaymentProvider.Demo), StringComparison.OrdinalIgnoreCase)
            || configured.Equals("Fake", StringComparison.OrdinalIgnoreCase))
            return PaymentProvider.Demo;

        throw new InvalidOperationException($"{ConfigKey} غير معروف: '{configured}' (المسموح: Stripe أو Demo).");
    }

    // بيئتا التطوير المحلي والاختبارات الآلية — لا زبائن حقيقيون فيهما.
    public static bool IsLocal(string environmentName) =>
        environmentName.Equals("Development", StringComparison.OrdinalIgnoreCase)
        || environmentName.Equals("Testing", StringComparison.OrdinalIgnoreCase);
}
