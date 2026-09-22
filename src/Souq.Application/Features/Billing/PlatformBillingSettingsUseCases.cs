using FluentValidation;
using MediatR;
using Souq.Application.Common.Auditing;
using Souq.Application.Common.Models;
using Souq.Domain.Interfaces;
using Souq.Domain.Platform;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Features.Billing;

// ============================================================================
// إعدادُ فوترة المنصّة (C5، [ADR-0056](0056)): العملةُ والمُصدِرُ ومهلُ السداد وتعليماتُ الدفع
// واختيارُ ملفّ الضريبة.
//
// **وهذا هو الموضع الذي يصير فيه قرارُ المالك `C-15` واقعاً.** الجوابُ كان «الدينار الأردنيّ»،
// وهو يدخل من **هنا** لا من سطرٍ في الشيفرة: قاعدةُ الواجهة البيضاء تمنع كتابةَ رمز عملةٍ في
// شيفرة المنتج، ويحرسها `WhiteLabelSourceTests` فعلاً. فما بنته الهندسة هو الآلةُ التي تصحّ
// تحت أيّ عملة، والقيمةُ يضعها المشغّل ويراها في شاشته.
//
// **وبلا عملةٍ ومُصدِرٍ لا تُصدَر فاتورة** — يفشل مغلقاً ويقول لماذا.
// ============================================================================

public record GetPlatformBillingSettingsQuery : IRequest<PlatformBillingSettingsDto>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("billing.settings.viewed");
}

public class GetPlatformBillingSettingsHandler
    : IRequestHandler<GetPlatformBillingSettingsQuery, PlatformBillingSettingsDto>
{
    private readonly IPlatformBillingSettingsRepository _settings;
    private readonly ITaxProfileRepository _profiles;

    public GetPlatformBillingSettingsHandler(
        IPlatformBillingSettingsRepository settings, ITaxProfileRepository profiles)
    {
        _settings = settings; _profiles = profiles;
    }

    public async Task<PlatformBillingSettingsDto> Handle(GetPlatformBillingSettingsQuery q, CancellationToken ct)
    {
        var settings = await _settings.GetAsync(ct);
        if (settings is null)
            // لم يُنشَأ الصفُّ بعد: شكلٌ فارغٌ بقيمه الافتراضية وسببُ المنع مسمّى. أفضلُ من 404
            // على شاشةِ إعدادٍ لم تُفتح قطّ.
            return new PlatformBillingSettingsDto(
                null, null, null, null,
                PlatformBillingSettings.DefaultInvoicePrefix, PlatformBillingSettings.DefaultCreditNotePrefix,
                PlatformBillingSettings.DefaultPaymentTermsDays, PlatformBillingSettings.DefaultGracePeriodDays,
                null, null, null, null, false,
                false, BillingBlockingReasons.SettingsMissing, TaxCollectionReasonsFor(null, false, null));

        var profile = settings.TaxProfileId is int id ? await _profiles.GetWithVersionsAsync(id, ct) : null;

        return new PlatformBillingSettingsDto(
            settings.Currency, settings.IssuerName, settings.IssuerAddress, settings.IssuerTaxNumber,
            settings.InvoiceNumberPrefix, settings.CreditNoteNumberPrefix,
            settings.PaymentTermsDays, settings.GracePeriodDays, settings.PaymentInstructions,
            settings.TaxProfileId, profile?.Jurisdiction, profile?.Name, settings.TaxCollectionEnabled,
            settings.CanIssue, BlockingReasonFor(settings),
            TaxCollectionReasonsFor(settings.TaxProfileId, settings.TaxCollectionEnabled, profile));
    }

    // السببُ الأوّل الذي يمنع الإصدار، بترتيب ما يُملأ أوّلاً. واحدٌ في كل مرّة: قائمةُ نواقصَ
    // في شاشةٍ تُقرأ أسوأ من نقصٍ واحد يُعالَج ثمّ يظهر التالي.
    internal static string? BlockingReasonFor(PlatformBillingSettings settings) =>
        string.IsNullOrEmpty(settings.Currency) ? BillingBlockingReasons.CurrencyNotSet
        : string.IsNullOrEmpty(settings.IssuerName) ? BillingBlockingReasons.IssuerNotSet
        : null;

    // سببُ عدم جمع الضريبة على فواتير المنصّة — بالرموز نفسها التي يستعملها متجر (ADR-0055):
    // مفرداتٌ واحدة لسؤالٍ واحد، ولو اختلف السائل.
    private static string TaxCollectionReasonsFor(int? profileId, bool enabled, TaxProfile? profile)
    {
        if (profileId is null) return Tax.TaxCollectionReasons.NoProfileSelected;
        if (!enabled) return Tax.TaxCollectionReasons.CollectionDisabled;
        var version = profile?.Versions
            .Where(v => v.Status == TaxProfileVersionStatus.Published)
            .OrderByDescending(v => v.EffectiveFrom).ThenByDescending(v => v.Version)
            .FirstOrDefault();
        if (version is null) return Tax.TaxCollectionReasons.NoEffectiveVersion;
        return version.AllowsCollection
            ? Tax.TaxCollectionReasons.Collecting
            : Tax.TaxCollectionReasons.VersionNotVerified;
    }
}

// ============================================================================
// ضبطُ الإعداد. يُنشئ الصفَّ إن لم يوجد — صفٌّ واحد عالميّ، فلا «إنشاء» منفصلٌ عن «تعديل».
//
// **ولا يُقبَل تغييرُ العملة بعد أن تصدر فاتورةٌ واحدة**: فواتيرُ صدرت بعملةٍ ودفترٌ يقول عملةً
// أخرى وضعٌ لا يُصلحه شيء. والفحصُ هنا لا في المجال لأنّه يسأل القاعدةَ عمّا صدر.
// ============================================================================
public record UpdatePlatformBillingSettingsCommand(PlatformBillingSettingsInput Settings)
    : IRequest<Result>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("billing.settings.updated", "PlatformBillingSettings", null,
        Metadata: new Dictionary<string, object?>
        {
            ["currency"] = Settings?.Currency,
            ["issuerName"] = Settings?.IssuerName,
            ["paymentTermsDays"] = Settings?.PaymentTermsDays,
            ["gracePeriodDays"] = Settings?.GracePeriodDays,
            ["taxProfileId"] = Settings?.TaxProfileId,
            ["taxCollectionEnabled"] = Settings?.TaxCollectionEnabled,
        });
}

public sealed class UpdatePlatformBillingSettingsValidator : AbstractValidator<UpdatePlatformBillingSettingsCommand>
{
    public UpdatePlatformBillingSettingsValidator()
    {
        RuleFor(x => x.Settings).NotNull();
        When(x => x.Settings is not null, () =>
        {
            RuleFor(x => x.Settings.Currency).MaximumLength(3);
            RuleFor(x => x.Settings.IssuerName).MaximumLength(PlatformBillingSettings.IssuerNameMaxLength);
            RuleFor(x => x.Settings.IssuerAddress).MaximumLength(PlatformBillingSettings.IssuerAddressMaxLength);
            RuleFor(x => x.Settings.IssuerTaxNumber).MaximumLength(PlatformBillingSettings.TaxNumberMaxLength);
            RuleFor(x => x.Settings.PaymentInstructions)
                .MaximumLength(PlatformBillingSettings.PaymentInstructionsMaxLength);
            RuleFor(x => x.Settings.PaymentTermsDays)
                .InclusiveBetween(0, PlatformBillingSettings.MaxPaymentTermsDays);
            RuleFor(x => x.Settings.GracePeriodDays)
                .InclusiveBetween(0, PlatformBillingSettings.MaxGracePeriodDays);
        });
    }
}

public class UpdatePlatformBillingSettingsHandler : IRequestHandler<UpdatePlatformBillingSettingsCommand, Result>
{
    private readonly IPlatformBillingSettingsRepository _settings;
    private readonly ITaxProfileRepository _profiles;
    private readonly IPlatformBillingQueries _queries;
    private readonly IUnitOfWork _uow;

    public UpdatePlatformBillingSettingsHandler(
        IPlatformBillingSettingsRepository settings, ITaxProfileRepository profiles,
        IPlatformBillingQueries queries, IUnitOfWork uow)
    {
        _settings = settings; _profiles = profiles; _queries = queries; _uow = uow;
    }

    public async Task<Result> Handle(UpdatePlatformBillingSettingsCommand cmd, CancellationToken ct)
    {
        var input = cmd.Settings;

        if (input.TaxProfileId is int profileId && await _profiles.GetByIdAsync(profileId, ct) is null)
            return Result.Failure(Error.NotFound("ملفّ الضريبة غير موجود"));

        var settings = await _settings.GetAsync(ct);
        if (settings is null)
        {
            settings = PlatformBillingSettings.Empty();
            _settings.Add(settings);
        }
        else if (!string.IsNullOrEmpty(settings.Currency)
                 && !string.Equals(settings.Currency, input.Currency?.Trim().ToUpperInvariant(), StringComparison.Ordinal)
                 && await _queries.AnyIssuedInvoiceAsync(ct))
        {
            return Result.Failure(Error.BusinessRule("BillingCurrencyLocked",
                "صدرت فواتيرُ بهذه العملة — تغييرُها يجعل الدفترَ يقول شيئاً وفواتيرُه شيئاً آخر"));
        }

        settings.SetCurrency(input.Currency);
        settings.SetIssuer(input.IssuerName, input.IssuerAddress, input.IssuerTaxNumber);
        settings.SetNumberPrefixes(input.InvoiceNumberPrefix, input.CreditNoteNumberPrefix);
        settings.SetTerms(input.PaymentTermsDays, input.GracePeriodDays);
        settings.SetPaymentInstructions(input.PaymentInstructions);
        settings.SelectTaxProfile(input.TaxProfileId, input.TaxCollectionEnabled);

        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}

// ============================================================================
// ما تحتاجه كلُّ حالةِ إصدارٍ من الإعداد، مقروءاً مرّةً وبفحصٍ واحد. يعيش هنا لا في كل معالج:
// ثلاثةُ معالجاتٍ تُصدِر مستنداً، ونسخُ الفحص فيها كان سيجعل أحدَها ينساه يوماً.
// ============================================================================
internal static class BillingSettingsGuard
{
    internal static Result<PlatformBillingSettings> RequireIssuable(PlatformBillingSettings? settings)
    {
        if (settings is null)
            return Result<PlatformBillingSettings>.Failure(Error.BusinessRule(
                BillingBlockingReasons.SettingsMissing, "لم يُضبَط إعدادُ فوترة المنصّة بعد"));

        var blocking = GetPlatformBillingSettingsHandler.BlockingReasonFor(settings);
        return blocking is null
            ? Result<PlatformBillingSettings>.Success(settings)
            : Result<PlatformBillingSettings>.Failure(Error.BusinessRule(blocking, blocking switch
            {
                BillingBlockingReasons.CurrencyNotSet =>
                    "اضبط عملةَ فوترة المنصّة قبل إصدار أيّ فاتورة",
                _ => "اضبط اسمَ المُصدِر في إعداد فوترة المنصّة قبل إصدار أيّ فاتورة",
            }));
    }

    // عملةُ الفوترة بعد التحقّق من وجودها. تُستدعى بعد `RequireIssuable` وحدها.
    internal static string CurrencyOf(PlatformBillingSettings settings) =>
        Money.Zero(settings.Currency!).Currency;
}
