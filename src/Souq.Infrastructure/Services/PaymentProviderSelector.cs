namespace Souq.Infrastructure.Services;

public enum PaymentProvider { Fake, Stripe }

// ============================================================================
// اختيار بوّابة الدفع عند الإقلاع (ADR-0020). البوّابة التجريبية تؤكّد كل دفع بلا مال؛ كانت
// تُختار تلقائياً حين يغيب مفتاح Stripe — بما فيها حزمة Docker التي تعمل بـ Production:
// أي زائر يطلب ويستلم طلباً "مدفوعاً" بلا دفع. وسيلة تطوير صارت ثغرة إنتاج بصمت.
// الآن: Development/Testing فقط تختارها ضمنياً. غيرهما يرفض الإقلاع برسالة واضحة ما لم تُطلب
// صراحةً (Payments:Provider=Fake — عرض توضيحي، مع تحذير في السجل عند كل إقلاع).
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
            if (IsLocal(environmentName)) return PaymentProvider.Fake;
            throw new InvalidOperationException(
                $"لا بوّابة دفع مضبوطة في بيئة {environmentName}: اضبط Stripe:SecretKey، " +
                $"أو {ConfigKey}=Fake صراحةً لعرض توضيحي بلا دفع حقيقي.");
        }

        if (configured.Equals(nameof(PaymentProvider.Stripe), StringComparison.OrdinalIgnoreCase))
            return hasStripeKey
                ? PaymentProvider.Stripe
                : throw new InvalidOperationException($"{ConfigKey}=Stripe لكن Stripe:SecretKey غير مضبوط.");

        if (configured.Equals(nameof(PaymentProvider.Fake), StringComparison.OrdinalIgnoreCase))
            return PaymentProvider.Fake;

        throw new InvalidOperationException($"{ConfigKey} غير معروف: '{configured}' (المسموح: Stripe أو Fake).");
    }

    // بيئتا التطوير المحلي والاختبارات الآلية — لا زبائن حقيقيون فيهما.
    public static bool IsLocal(string environmentName) =>
        environmentName.Equals("Development", StringComparison.OrdinalIgnoreCase)
        || environmentName.Equals("Testing", StringComparison.OrdinalIgnoreCase);
}
