using FluentValidation;
using MediatR;
using Souq.Application.Common.Auditing;
using Souq.Application.Common.Models;
using Souq.Domain.Interfaces;
using Souq.Domain.Platform;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Features.Tax;

// ============================================================================
// إدارةُ ملفّات الضريبة من المنصّة ([ADR-0055](0055)، قرار المالك P-06).
//
// **كلُّ ما هنا إعدادٌ، ولا شيء منه قاعدةُ قانون.** القيمُ تُدخَل وتُنشر وتبقى **غيرَ متحقَّقٍ
// منها** حتى يؤكّدها مهنيٌّ باسمه — ولا تُجمَع ضريبةٌ قبل ذلك. فالهندسة لا تضع `Verified` أبداً،
// وهذه الحالةُ لا تُضبَط إلا بالأمر الصريح أدناه، ومَن ينفّذه يُسمّي نفسه ويُدقَّق فعلُه.
//
// **وكلُّ أمرٍ هنا مُدقَّق** (`IAuditable`) — وهو أحد ثوابت ADR-0055 الستّة: تغييرُ إعدادٍ ضريبيّ
// يجب أن يكون معروفاً مَن فعله ومتى، لأنّ أثرَه مالٌ ومسؤوليةٌ قانونية.
// ============================================================================

internal static class TaxErrors
{
    public static Error ProfileNotFound => Error.NotFound("ملفّ الضريبة غير موجود");
    public static Error VersionNotFound => Error.NotFound("إصدار ملفّ الضريبة غير موجود");
    public static Error NoDraft => Error.BusinessRule("NoTaxDraft", "لا مسوّدة لهذا الملفّ — أنشئ إصداراً أولاً");

    public static Dictionary<string, object?> Meta(params (string Key, object? Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value);
}

// ── القراءة ─────────────────────────────────────────────────────────────────

public record ListTaxProfilesQuery : IRequest<IReadOnlyList<TaxProfileDto>>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("tax.profiles.listed");
}

public class ListTaxProfilesHandler : IRequestHandler<ListTaxProfilesQuery, IReadOnlyList<TaxProfileDto>>
{
    private readonly ITaxProfileRepository _profiles;
    public ListTaxProfilesHandler(ITaxProfileRepository profiles) => _profiles = profiles;

    public async Task<IReadOnlyList<TaxProfileDto>> Handle(ListTaxProfilesQuery q, CancellationToken ct) =>
        (await _profiles.ListWithVersionsAsync(ct)).Select(TaxMapper.ToDto).ToList();
}

public record GetTaxProfileQuery(int TaxProfileId) : IRequest<Result<TaxProfileDto>>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("tax.profile.viewed", "TaxProfile", TaxProfileId.ToString());
}

public class GetTaxProfileHandler : IRequestHandler<GetTaxProfileQuery, Result<TaxProfileDto>>
{
    private readonly ITaxProfileRepository _profiles;
    public GetTaxProfileHandler(ITaxProfileRepository profiles) => _profiles = profiles;

    public async Task<Result<TaxProfileDto>> Handle(GetTaxProfileQuery q, CancellationToken ct) =>
        await _profiles.GetWithVersionsAsync(q.TaxProfileId, ct) is { } profile
            ? Result<TaxProfileDto>.Success(TaxMapper.ToDto(profile))
            : Result<TaxProfileDto>.Failure(TaxErrors.ProfileNotFound);
}

// ── الإنشاء ─────────────────────────────────────────────────────────────────

// ملفٌّ لاختصاص، بلا إصدارٍ بعد. ملفٌّ ثانٍ للاختصاص نفسه مرفوض: التصحيحُ إصدار لا ملفّ.
public record CreateTaxProfileCommand(string Jurisdiction, string Name) : IRequest<Result<int>>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("tax.profile.created", "TaxProfile",
        Jurisdiction?.Trim().ToUpperInvariant(), Metadata: TaxErrors.Meta(("name", Name)));
}

public sealed class CreateTaxProfileValidator : AbstractValidator<CreateTaxProfileCommand>
{
    public CreateTaxProfileValidator()
    {
        RuleFor(x => x.Jurisdiction).NotEmpty().MaximumLength(TaxProfile.JurisdictionMaxLength);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(TaxProfile.NameMaxLength);
    }
}

public class CreateTaxProfileHandler : IRequestHandler<CreateTaxProfileCommand, Result<int>>
{
    private readonly ITaxProfileRepository _profiles;
    private readonly IUnitOfWork _uow;

    public CreateTaxProfileHandler(ITaxProfileRepository profiles, IUnitOfWork uow)
    {
        _profiles = profiles; _uow = uow;
    }

    public async Task<Result<int>> Handle(CreateTaxProfileCommand cmd, CancellationToken ct)
    {
        if (await _profiles.FindByJurisdictionAsync(cmd.Jurisdiction, ct) is not null)
            return Result<int>.Failure(Error.Conflict("DuplicateTaxProfile", "لهذا الاختصاص ملفٌّ قائم — أضف إصداراً إليه"));

        var profile = new TaxProfile(cmd.Jurisdiction, cmd.Name);
        await _profiles.AddAsync(profile, ct);
        await _uow.SaveChangesAsync(ct);
        return Result<int>.Success(profile.Id);
    }
}

// ============================================================================
// مسوّدةُ إصدارٍ جديد بقيمها. تبدأ **غيرَ متحقَّقٍ منها** دائماً — ولا مدخلَ في هذا الأمر يستطيع
// تغيير ذلك: حالةُ التحقّق ليست حقلاً يُرسله مُنادٍ، بل فعلٌ منفصل يُسمّي فاعلَه.
// ============================================================================
public record AddTaxProfileVersionCommand(int TaxProfileId, TaxProfileVersionInput Version)
    : IRequest<Result<int>>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("tax.profile.version.drafted", "TaxProfile", TaxProfileId.ToString(),
        Metadata: TaxErrors.Meta(
            ("effectiveFrom", Version?.EffectiveFrom), ("priceMode", Version?.PriceMode),
            ("shippingTaxable", Version?.ShippingTaxable), ("rates", Version?.Rates?.Count)));
}

public sealed class AddTaxProfileVersionValidator : AbstractValidator<AddTaxProfileVersionCommand>
{
    public AddTaxProfileVersionValidator()
    {
        RuleFor(x => x.Version).NotNull();
        RuleFor(x => x.Version.PriceMode).NotEmpty()
            .Must(mode => Enum.TryParse<TaxPriceMode>(mode, ignoreCase: true, out _))
            .WithMessage("عُرف السعر إمّا Inclusive أو Exclusive")
            .When(x => x.Version is not null);
        RuleFor(x => x.Version.Rates).NotEmpty().WithMessage("الإصدار يحتاج نسبةً واحدة على الأقل")
            .When(x => x.Version is not null);
    }
}

public class AddTaxProfileVersionHandler : IRequestHandler<AddTaxProfileVersionCommand, Result<int>>
{
    private readonly ITaxProfileRepository _profiles;
    private readonly IUnitOfWork _uow;

    public AddTaxProfileVersionHandler(ITaxProfileRepository profiles, IUnitOfWork uow)
    {
        _profiles = profiles; _uow = uow;
    }

    public async Task<Result<int>> Handle(AddTaxProfileVersionCommand cmd, CancellationToken ct)
    {
        var profile = await _profiles.GetWithVersionsAsync(cmd.TaxProfileId, ct);
        if (profile is null) return Result<int>.Failure(TaxErrors.ProfileNotFound);

        var input = cmd.Version;
        var mode = Enum.Parse<TaxPriceMode>(input.PriceMode, ignoreCase: true);
        var version = profile.AddDraft(input.EffectiveFrom, mode, input.ShippingTaxable);

        version.SetRates(input.Rates.Select(r => new TaxRate(r.Code, r.Name, r.BasisPoints, r.Category)).ToList());
        version.SetThreshold(input.RegistrationThreshold is { } t ? new Money(t.Amount, t.Currency) : null);
        version.SetNotes(input.Notes);

        await _uow.SaveChangesAsync(ct);
        return Result<int>.Success(version.Id);
    }
}

// النشرُ تجميد: بعده لا تُعدَّل قيمةٌ واحدة، والتصحيحُ إصدارٌ جديد بتاريخ نفاذه.
public record PublishTaxProfileVersionCommand(int TaxProfileId, int VersionId) : IRequest<Result>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("tax.profile.version.published", "TaxProfile", TaxProfileId.ToString(),
        Metadata: TaxErrors.Meta(("versionId", VersionId)));
}

public class PublishTaxProfileVersionHandler : IRequestHandler<PublishTaxProfileVersionCommand, Result>
{
    private readonly ITaxProfileRepository _profiles;
    private readonly IUnitOfWork _uow;

    public PublishTaxProfileVersionHandler(ITaxProfileRepository profiles, IUnitOfWork uow)
    {
        _profiles = profiles; _uow = uow;
    }

    public async Task<Result> Handle(PublishTaxProfileVersionCommand cmd, CancellationToken ct)
    {
        var profile = await _profiles.GetWithVersionsAsync(cmd.TaxProfileId, ct);
        if (profile is null) return Result.Failure(TaxErrors.ProfileNotFound);

        var version = profile.Versions.FirstOrDefault(v => v.Id == cmd.VersionId);
        if (version is null) return Result.Failure(TaxErrors.VersionNotFound);

        version.Publish();
        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}

// ============================================================================
// **التحقّق: الفعلُ الوحيد الذي يسمح بجمع ضريبة** — ولذلك يُسمّي فاعله ويُدقَّق.
//
// ومَن يضعه إنسانٌ يقرأ الأرقام لا خطُّ أنابيب: قرار المالك يقول إنّ قيمَ الملفّ تحتاج **تحقّقاً
// من محاسبٍ أو جهةٍ ضريبية أو مستشارٍ قانونيّ قبل الاستخدام التجاري**، وهذا الأمر هو تسجيلُ ذلك
// التحقّق. لا يجري في هجرة، ولا في بذر، ولا افتراضاً.
// ============================================================================
public record VerifyTaxProfileVersionCommand(int TaxProfileId, int VersionId, string VerifiedBy, string? Note)
    : IRequest<Result>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("tax.profile.version.verified", "TaxProfile", TaxProfileId.ToString(),
        Metadata: TaxErrors.Meta(("versionId", VersionId), ("verifiedBy", VerifiedBy)));
}

public sealed class VerifyTaxProfileVersionValidator : AbstractValidator<VerifyTaxProfileVersionCommand>
{
    public VerifyTaxProfileVersionValidator()
    {
        RuleFor(x => x.VerifiedBy).NotEmpty().MaximumLength(TaxVerification.ByMaxLength);
        RuleFor(x => x.Note).MaximumLength(TaxVerification.NoteMaxLength);
    }
}

public class VerifyTaxProfileVersionHandler : IRequestHandler<VerifyTaxProfileVersionCommand, Result>
{
    private readonly ITaxProfileRepository _profiles;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public VerifyTaxProfileVersionHandler(ITaxProfileRepository profiles, IUnitOfWork uow, TimeProvider clock)
    {
        _profiles = profiles; _uow = uow; _clock = clock;
    }

    public async Task<Result> Handle(VerifyTaxProfileVersionCommand cmd, CancellationToken ct)
    {
        var profile = await _profiles.GetWithVersionsAsync(cmd.TaxProfileId, ct);
        if (profile is null) return Result.Failure(TaxErrors.ProfileNotFound);

        var version = profile.Versions.FirstOrDefault(v => v.Id == cmd.VersionId);
        if (version is null) return Result.Failure(TaxErrors.VersionNotFound);

        version.Verify(cmd.VerifiedBy, _clock.GetUtcNow().UtcDateTime, cmd.Note);
        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}

// سحبُ التحقّق: يتوقّف الجمع فوراً، ولا يُمسّ إصدارٌ ولا طلبٌ احتُسب به — لقطاتُ الطلبات تحمل
// ما طُبِّق فعلاً، وتاريخُها لا يتغيّر بهذا.
public record RequireTaxConfirmationCommand(int TaxProfileId, int VersionId, string? Note)
    : IRequest<Result>, IAuditable
{
    // الاسم بشُرَطٍ لا بشُرَطٍ سفلية: نمطُ أسماء التدقيق `^[a-z]+(?:[.-][a-z]+)+$`، وقد رفض
    // `confirmation_required` بـ 500 في اختبار التكامل — أمسكه قبل أن يُمسكه مستخدم.
    public AuditRecord ToAuditRecord() => new("tax.profile.version.unverified", "TaxProfile",
        TaxProfileId.ToString(), Metadata: TaxErrors.Meta(("versionId", VersionId)));
}

public sealed class RequireTaxConfirmationValidator : AbstractValidator<RequireTaxConfirmationCommand>
{
    public RequireTaxConfirmationValidator() =>
        RuleFor(x => x.Note).MaximumLength(TaxVerification.NoteMaxLength);
}

public class RequireTaxConfirmationHandler : IRequestHandler<RequireTaxConfirmationCommand, Result>
{
    private readonly ITaxProfileRepository _profiles;
    private readonly IUnitOfWork _uow;

    public RequireTaxConfirmationHandler(ITaxProfileRepository profiles, IUnitOfWork uow)
    {
        _profiles = profiles; _uow = uow;
    }

    public async Task<Result> Handle(RequireTaxConfirmationCommand cmd, CancellationToken ct)
    {
        var profile = await _profiles.GetWithVersionsAsync(cmd.TaxProfileId, ct);
        if (profile is null) return Result.Failure(TaxErrors.ProfileNotFound);

        var version = profile.Versions.FirstOrDefault(v => v.Id == cmd.VersionId);
        if (version is null) return Result.Failure(TaxErrors.VersionNotFound);

        version.RequireConfirmation(cmd.Note);
        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
