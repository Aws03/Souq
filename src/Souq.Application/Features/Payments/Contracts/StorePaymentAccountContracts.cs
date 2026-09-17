using FluentValidation;
using Souq.Application.Common.Models;
using Souq.Domain.Entities;

namespace Souq.Application.Features.Payments.Contracts;

// ============================================================================
// عقد محرّر حساب بوّابة المتجر (المرحلة 11، D-13؛ استُخرج في التدقيق المعماري M1 — TD-04/R-04، ModuleBoundaryAudit.md
// "الفخّ" في ترتيب 3): منطقة المنصّة (Features/Platform/TenantPaymentAccounts.cs) تدخل نطاق المتجر المستهدف عبر
// ITenantScopeRunner وتطلب هذا العقد وحده — لا صنف Payments الفعلي (StorePaymentAccountEditor) ولا كياناته مباشرة.
// نقل الملفّين معاً بلا عقد كان سيُبقي Platform تشير لصنف Payments خارج .Contracts (يُفشل حدود الوحدات)، ويُخرج
// TenantId من نطاق Features.Platform الوحيد المسموح له بحمله (MultiTenancy.md §2) لو انتقلت أوامر المنصّة نفسها.
// هذا العقد هو ما يجعل Platform → Payments عبوراً عبر عقد منشور (الصنف A) لا كتابةً مباشرة لتجميع وحدة أخرى.
// ============================================================================

// SecretKey/WebhookSecret فارغان ⇒ يبقى المحفوظ (التعديل لا يلزم إعادة إدخال السرّ).
public record StorePaymentAccountInput(string PublishableKey, string? SecretKey, string? WebhookSecret);

public record StorePaymentAccountDto(
    bool UsesStoreAccount, string? Provider, string? PublishableKey, bool? LiveMode, string? SecretKeyHint,
    bool HasWebhookSecret, DateTime? UpdatedAt, bool CanStoreSecrets, bool TestKeysAllowed);

public sealed class StorePaymentAccountInputValidator : AbstractValidator<StorePaymentAccountInput>
{
    public StorePaymentAccountInputValidator()
    {
        RuleFor(x => x.PublishableKey).NotEmpty().MaximumLength(StorePaymentAccount.KeyMaxLength);
        RuleFor(x => x.SecretKey).MaximumLength(StorePaymentAccount.KeyMaxLength);
        RuleFor(x => x.WebhookSecret).MaximumLength(StorePaymentAccount.KeyMaxLength);
    }
}

// المحرّر الواحد لمساري المتجر والمنصّة — تنفّذه Payments (StorePaymentAccountEditor)؛ Platform يستدعيه عبر
// ITenantScopeRunner بهذا العقد فقط، فيُحلّ من نطاق المتجر المستهدف الذي يفتحه ذلك المشغّل.
public interface IStorePaymentAccountEditor
{
    Task<StorePaymentAccountDto> GetAsync(CancellationToken ct);
    Task<Result> SaveAsync(StorePaymentAccountInput input, CancellationToken ct);
    Task<Result> RemoveAsync(CancellationToken ct);
}

// ما يدخل سجلّ التدقيق: ما تغيّر لا المفاتيح نفسها. يستخدمه مساركا المتجر والمنصّة معاً.
public static class StorePaymentAudit
{
    public static Dictionary<string, object?> Meta(StorePaymentAccountInput? input) => new()
    {
        ["liveMode"] = PaymentKeyRules.PublishableMode(input?.PublishableKey),
        ["secretKeyChanged"] = !string.IsNullOrWhiteSpace(input?.SecretKey),
        ["webhookSecretChanged"] = !string.IsNullOrWhiteSpace(input?.WebhookSecret),
    };
}
