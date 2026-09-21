using FluentValidation;
using MediatR;
using Souq.Application.Common.Auditing;
using Souq.Application.Common.Models;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Tax;

// ============================================================================
// ما يملكه المتجرُ من ضريبته: **الاختيار**، لا القواعد ([ADR-0055](0055)).
//
// يختار ملفَّ اختصاصه، ويفعّل الجمع، ويكتب رقمَ تسجيله. ولا يُدخل نسبةً واحدة — ولو كان يستطيع
// لَما كان لتحقّقٍ مركزيّ معنى، ولَعاد تشتُّتُ القيم متجراً متجراً الذي وُجد الملفّ ليُنهيه.
//
// **وقراءتُه تقول له لماذا لا يُجمَع شيء** إن لم يُجمَع: لم يختر، أو اختار ولم يفعّل، أو فعّل
// وملفُّه ينتظر محاسباً. صفرٌ بلا سببٍ يقرأ كأنه عطب.
// ============================================================================

public record GetStoreTaxSettingsQuery : IRequest<StoreTaxSettingsDto>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("store.tax.settings.viewed");
}

public class GetStoreTaxSettingsHandler : IRequestHandler<GetStoreTaxSettingsQuery, StoreTaxSettingsDto>
{
    private readonly IStoreTaxSettingsRepository _settings;
    private readonly ITaxProfileRepository _profiles;
    private readonly TimeProvider _clock;

    public GetStoreTaxSettingsHandler(
        IStoreTaxSettingsRepository settings, ITaxProfileRepository profiles, TimeProvider clock)
    {
        _settings = settings; _profiles = profiles; _clock = clock;
    }

    public async Task<StoreTaxSettingsDto> Handle(GetStoreTaxSettingsQuery q, CancellationToken ct)
    {
        var settings = await _settings.GetAsync(ct);
        return await StoreTaxView.BuildAsync(settings, _profiles, _clock.GetUtcNow().UtcDateTime, ct);
    }
}

public record UpdateStoreTaxSettingsCommand(StoreTaxSettingsInput Settings)
    : IRequest<Result<StoreTaxSettingsDto>>, IAuditable
{
    // مُدقَّق: تغييرُ إعدادٍ ضريبيّ أثرُه مالٌ ومسؤوليةٌ قانونية، فمَن غيّره ومتى يجب أن يُعرَف
    // (أحد ثوابت ADR-0055 الستّة).
    public AuditRecord ToAuditRecord() => new("store.tax.settings.updated", Metadata: new Dictionary<string, object?>
    {
        ["taxProfileId"] = Settings?.TaxProfileId,
        ["collectionEnabled"] = Settings?.CollectionEnabled,
    });
}

public sealed class UpdateStoreTaxSettingsValidator : AbstractValidator<UpdateStoreTaxSettingsCommand>
{
    public UpdateStoreTaxSettingsValidator()
    {
        RuleFor(x => x.Settings).NotNull();
        RuleFor(x => x.Settings.RegistrationNumber)
            .MaximumLength(StoreTaxSettings.RegistrationNumberMaxLength)
            .When(x => x.Settings is not null);
    }
}

public class UpdateStoreTaxSettingsHandler
    : IRequestHandler<UpdateStoreTaxSettingsCommand, Result<StoreTaxSettingsDto>>
{
    private readonly IStoreTaxSettingsRepository _settings;
    private readonly ITaxProfileRepository _profiles;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public UpdateStoreTaxSettingsHandler(
        IStoreTaxSettingsRepository settings, ITaxProfileRepository profiles, IUnitOfWork uow, TimeProvider clock)
    {
        _settings = settings; _profiles = profiles; _uow = uow; _clock = clock;
    }

    public async Task<Result<StoreTaxSettingsDto>> Handle(
        UpdateStoreTaxSettingsCommand cmd, CancellationToken ct)
    {
        var input = cmd.Settings;
        var now = _clock.GetUtcNow().UtcDateTime;

        // ملفٌّ لا وجود له يُرفض: اختيارٌ صامتٌ لمعرّفٍ خاطئ يترك المتجر يظنّ أنه ضبط شيئاً.
        if (input.TaxProfileId is int profileId
            && await _profiles.GetWithVersionsAsync(profileId, ct) is null)
            return Result<StoreTaxSettingsDto>.Failure(TaxErrors.ProfileNotFound);

        var settings = await _settings.GetAsync(ct);
        if (settings is null)
        {
            settings = StoreTaxSettings.None();
            _settings.Add(settings);
        }

        settings.SelectProfile(input.TaxProfileId, now);
        settings.SetRegistrationNumber(input.RegistrationNumber);
        settings.SetCollection(input.CollectionEnabled);

        await _uow.SaveChangesAsync(ct);
        return Result<StoreTaxSettingsDto>.Success(
            await StoreTaxView.BuildAsync(settings, _profiles, now, ct));
    }
}

// ============================================================================
// بناءُ ما يُقرأ، **مع سببه**. موضعٌ واحد يشتقّ «هل يُجمَع؟» ولماذا — فلا تفترق قراءةٌ عن كتابةٍ
// في الجواب، ولا تُعيد الواجهة اشتقاقَه بنسخةٍ ثانية من القاعدة.
// ============================================================================
internal static class StoreTaxView
{
    public static async Task<StoreTaxSettingsDto> BuildAsync(
        StoreTaxSettings? settings, ITaxProfileRepository profiles, DateTime now, CancellationToken ct)
    {
        if (settings?.TaxProfileId is not int profileId)
            return new StoreTaxSettingsDto(
                null, null, null, false, settings?.RegistrationNumber, null, null,
                Collecting: false, Reason: TaxCollectionReasons.NoProfileSelected);

        var profile = await profiles.GetWithVersionsAsync(profileId, ct);
        var version = profile?.VersionOn(now);
        var versionDto = version is null ? null : TaxMapper.ToDto(version);

        var reason = (settings.CollectionEnabled, version) switch
        {
            (false, _) => TaxCollectionReasons.CollectionDisabled,
            (true, null) => TaxCollectionReasons.NoEffectiveVersion,
            (true, var v) when !v!.AllowsCollection => TaxCollectionReasons.VersionNotVerified,
            _ => TaxCollectionReasons.Collecting,
        };

        return new StoreTaxSettingsDto(
            profileId, profile?.Jurisdiction, profile?.Name, settings.CollectionEnabled,
            settings.RegistrationNumber, settings.SelectedAt, versionDto,
            Collecting: reason == TaxCollectionReasons.Collecting, Reason: reason);
    }
}
