namespace Souq.Application.Common.Interfaces;

// ============================================================================
// تشفير أسرار المتاجر عند التخزين (المرحلة 11، ADR-0031): مفاتيح بوّابة الدفع لكل متجر. الغرض (purpose) يربط النصّ
// المشفّر بمالكه ("tenant:5:stripe:secret-key"): نصّ منسوخ إلى صفّ متجر آخر لا يُفكّ. المفتاح الرئيسي من الإعداد (سرّ
// بيئة، لا appsettings) — بدونه لا تُحفظ أسرار متاجر (IsConfigured = false) ويعمل حساب النشر وحده.
// ============================================================================
public interface ISecretProtector
{
    bool IsConfigured { get; }

    string Protect(string plaintext, string purpose);

    // يرمي SecretUnavailableException إن تعذّر الفكّ: مفتاح أُزيل بعد تدوير، نصّ تالف، أو غرض لا يطابق.
    string Unprotect(string protectedText, string purpose);
}

public sealed class SecretUnavailableException : Exception
{
    public SecretUnavailableException(string message, Exception? inner = null) : base(message, inner) { }
}

public static class SecretPurposes
{
    public static string StripeSecretKey(int tenantId) => $"tenant:{tenantId}:stripe:secret-key";
    public static string StripeWebhookSecret(int tenantId) => $"tenant:{tenantId}:stripe:webhook-secret";
}
