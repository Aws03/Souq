using FluentValidation;
using MediatR;
using Souq.Application.Common.Auditing;
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Models;
using Souq.Application.Common.Security;
using Souq.Application.Common.Tenancy;
using Souq.Application.Features.Payments.Contracts;
using Souq.Domain.Exceptions;
using Souq.Domain.Interfaces;
using Souq.Domain.Entities;

namespace Souq.Application.Features.Payments;

// ============================================================================
// حساب بوّابة الدفع الخاص بالمتجر (المرحلة 11، D-13؛ نُقل من Features/Stores إلى وحدته الصحيحة Payments في التدقيق
// المعماري M1 — TD-04/R-04: السرّ الأشدّ حساسية في النظام كان منسوباً لوحدة Platform بالخطأ). مدير المتجر
// (store.payments.manage) أو المنصّة (عبر عقد IStorePaymentAccountEditor في Contracts) يربطان مفاتيح Stripe
// للمتجر فيُقبض ماله في حسابه؛ بدونها يعمل حساب النشر. السرّان يُشفَّران مربوطَين بالمتجر ولا يُعادان في أي ردّ ولا يدخلان
// سجلّ التدقيق — الواجهة ترى تلميح المفتاح وهل سرّ الإشعارات مضبوط فقط.
// ============================================================================

// سياسة مفاتيح المتاجر (يسجّلها Infrastructure من الإعداد): خارج التطوير والاختبار لا تُقبل مفاتيح Stripe التجريبية إلا
// بإذن صريح — مفتاح تجريبي على خادم حقيقي يجعل بطاقات الاختبار "تدفع" طلبات حقيقية بلا مال (دفع وهمي صامت).
public sealed class StorePaymentPolicy
{
    public bool AllowTestKeys { get; init; }
}

// ينفّذ IStorePaymentAccountEditor (Contracts) — Platform يستدعيه بذلك العقد وحده عبر ITenantScopeRunner.
public sealed class StorePaymentAccountEditor : IStorePaymentAccountEditor
{
    private readonly IStorePaymentAccountRepository _accounts;
    private readonly ISecretProtector _secrets;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUser _currentUser;
    private readonly StorePaymentPolicy _policy;
    private readonly IUnitOfWork _uow;

    public StorePaymentAccountEditor(
        IStorePaymentAccountRepository accounts, ISecretProtector secrets, ITenantContext tenant, ICurrentUser currentUser,
        StorePaymentPolicy policy, IUnitOfWork uow)
    {
        _accounts = accounts; _secrets = secrets; _tenant = tenant; _currentUser = currentUser; _policy = policy; _uow = uow;
    }

    public async Task<StorePaymentAccountDto> GetAsync(CancellationToken ct)
    {
        var account = await _accounts.GetAsync(ct);
        return new StorePaymentAccountDto(
            account is not null, account?.Provider, account?.PublishableKey, account?.LiveMode, account?.SecretKeyHint,
            account?.WebhookSecretCipher is not null, account?.UpdatedAt ?? account?.CreatedAt,
            _secrets.IsConfigured, _policy.AllowTestKeys);
    }

    public async Task<Result> SaveAsync(StorePaymentAccountInput input, CancellationToken ct)
    {
        if (!_secrets.IsConfigured)
            return Result.Failure(Error.Unavailable("SecretsNotConfigured",
                "تشفير أسرار المتاجر غير مضبوط على هذا الخادم (Secrets:*) — لا تُحفظ مفاتيح متجر"));

        var live = PaymentKeyRules.PublishableMode(input.PublishableKey)
                   ?? throw Invalid("المفتاح العلني بصيغة Stripe: pk_test_ أو pk_live_");
        var secret = Blank(input.SecretKey);
        if (secret is not null && PaymentKeyRules.SecretMode(secret) != live)
            throw Invalid("المفتاح السرّي بصيغة Stripe (sk_ أو rk_) وبوضع المفتاح العلني نفسه (تجريبي أو حقيقي)");
        var webhook = Blank(input.WebhookSecret);
        if (webhook is not null && !PaymentKeyRules.IsWebhookSecret(webhook))
            throw Invalid("سرّ الإشعارات بصيغة Stripe: whsec_");
        if (!live && !_policy.AllowTestKeys)
            return Result.Failure(Error.BusinessRule("TestKeysNotAllowed",
                "مفاتيح Stripe التجريبية غير مسموحة على هذا الخادم: بطاقات الاختبار لا تدفع مالاً حقيقياً"));

        var tenantId = _tenant.RequireTenant().Id;
        var secretCipher = secret is null ? null : _secrets.Protect(secret, SecretPurposes.StripeSecretKey(tenantId));
        var hint = secret is null ? null : PaymentKeyRules.Hint(secret);
        var webhookCipher = webhook is null ? null : _secrets.Protect(webhook, SecretPurposes.StripeWebhookSecret(tenantId));

        var account = await _accounts.GetAsync(ct);
        if (account is null)
        {
            if (secretCipher is null) throw Invalid("المفتاح السرّي مطلوب عند ربط حساب المتجر");
            await _accounts.AddAsync(
                new StorePaymentAccount(input.PublishableKey, secretCipher, hint!, live, webhookCipher, _currentUser.UserId), ct);
        }
        else
        {
            account.Update(input.PublishableKey, live, secretCipher, hint, webhookCipher, _currentUser.UserId);
        }

        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }

    // فكّ الربط: يعود المتجر لحساب النشر. دفعات أخذها حسابه لا تُستردّ حتى يُعاد ربطه (ADR-0031).
    public async Task<Result> RemoveAsync(CancellationToken ct)
    {
        var account = await _accounts.GetAsync(ct);
        if (account is null) return Result.Success();
        _accounts.Remove(account);
        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }

    private static InvalidPaymentOperationException Invalid(string message) => new(message, "InvalidPaymentKeys");

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}

public record GetStorePaymentAccountQuery : IRequest<StorePaymentAccountDto>;

public class GetStorePaymentAccountHandler : IRequestHandler<GetStorePaymentAccountQuery, StorePaymentAccountDto>
{
    private readonly IStorePaymentAccountEditor _editor;
    public GetStorePaymentAccountHandler(IStorePaymentAccountEditor editor) => _editor = editor;

    public Task<StorePaymentAccountDto> Handle(GetStorePaymentAccountQuery query, CancellationToken ct) => _editor.GetAsync(ct);
}

public record UpdateStorePaymentAccountCommand(StorePaymentAccountInput Account) : IRequest<Result>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("store.payments.updated", "Tenant", Metadata: StorePaymentAudit.Meta(Account));
}

public sealed class UpdateStorePaymentAccountValidator : AbstractValidator<UpdateStorePaymentAccountCommand>
{
    public UpdateStorePaymentAccountValidator() =>
        RuleFor(x => x.Account).NotNull().SetValidator(new StorePaymentAccountInputValidator());
}

public class UpdateStorePaymentAccountHandler : IRequestHandler<UpdateStorePaymentAccountCommand, Result>
{
    private readonly IStorePaymentAccountEditor _editor;
    public UpdateStorePaymentAccountHandler(IStorePaymentAccountEditor editor) => _editor = editor;

    public Task<Result> Handle(UpdateStorePaymentAccountCommand cmd, CancellationToken ct) => _editor.SaveAsync(cmd.Account, ct);
}

public record RemoveStorePaymentAccountCommand : IRequest<Result>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("store.payments.removed", "Tenant");
}

public class RemoveStorePaymentAccountHandler : IRequestHandler<RemoveStorePaymentAccountCommand, Result>
{
    private readonly IStorePaymentAccountEditor _editor;
    public RemoveStorePaymentAccountHandler(IStorePaymentAccountEditor editor) => _editor = editor;

    public Task<Result> Handle(RemoveStorePaymentAccountCommand cmd, CancellationToken ct) => _editor.RemoveAsync(ct);
}
