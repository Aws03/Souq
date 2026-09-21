using Souq.Domain.Platform;

namespace Souq.Application.Features.Tax;

// ============================================================================
// عقودُ ملفّ الضريبة كما تُقرأ وتُكتب ([ADR-0055](0055)).
//
// **حالةُ التحقّق في كل شكلٍ يُقرأ منه رقم.** ليست حقلاً إضافياً: هي الفرقُ بين «قيمةٌ في القاعدة»
// و«قيمةٌ يُعتمد عليها». فلا تُقرأ نسبةٌ من هذه الواجهة بلا أن يظهر معها أنّ أحداً تحقّق منها —
// وذلك ما يجعل رقماً مبحوثاً عنه غيرَ قادرٍ على أن يبدو قاعدةً.
// ============================================================================

public sealed record TaxRateDto(string Code, string Name, int BasisPoints, string Category)
{
    // النسبةُ كما يقرؤها إنسان، مشتقّةً من نقاط الأساس لا محفوظةً بجانبها: 1600 ⇒ 16.
    public decimal Percent => BasisPoints / 100m;
}

public sealed record MoneyDto(decimal Amount, string Currency);

public sealed record TaxProfileVersionDto(
    int Id,
    int Version,
    DateTime EffectiveFrom,
    string Status,
    string PriceMode,
    bool ShippingTaxable,
    string VerificationState,
    string? VerifiedBy,
    DateTime? VerifiedAt,
    string? VerificationNote,
    MoneyDto? RegistrationThreshold,
    string? Notes,
    IReadOnlyList<TaxRateDto> Rates)
{
    // القاعدة الواحدة التي يقرؤها كلُّ مُستهلك: لا جمعَ إلا من منشورٍ ومُتحقَّقٍ منه.
    public bool AllowsCollection =>
        Status == nameof(TaxProfileVersionStatus.Published)
        && VerificationState == nameof(TaxVerificationState.Verified);
}

public sealed record TaxProfileDto(
    int Id, string Jurisdiction, string Name, IReadOnlyList<TaxProfileVersionDto> Versions)
{
    // الإصدار النافذ اليوم، إن وُجد — وهو ما يُختار له متجرٌ فعلاً.
    public TaxProfileVersionDto? Current => Versions
        .Where(v => v.Status == nameof(TaxProfileVersionStatus.Published))
        .OrderByDescending(v => v.EffectiveFrom).ThenByDescending(v => v.Version)
        .FirstOrDefault();

    public bool AnyVersionAllowsCollection => Versions.Any(v => v.AllowsCollection);
}

// ما يراه المتجر عن ضريبته: اختيارُه، وحالةُ ما اختاره، **وسببُ عدم الجمع إن لم يُجمَع**.
public sealed record StoreTaxSettingsDto(
    int? TaxProfileId,
    string? Jurisdiction,
    string? ProfileName,
    bool CollectionEnabled,
    string? RegistrationNumber,
    DateTime? SelectedAt,
    TaxProfileVersionDto? EffectiveVersion,
    bool Collecting,
    string Reason);

public sealed record StoreTaxSettingsInput(int? TaxProfileId, bool CollectionEnabled, string? RegistrationNumber);

public sealed record TaxRateInput(string Code, string Name, int BasisPoints, string? Category);

public sealed record TaxProfileVersionInput(
    DateTime EffectiveFrom,
    string PriceMode,
    bool ShippingTaxable,
    IReadOnlyList<TaxRateInput> Rates,
    MoneyDto? RegistrationThreshold = null,
    string? Notes = null);

public static class TaxMapper
{
    public static TaxProfileDto ToDto(TaxProfile profile) => new(
        profile.Id, profile.Jurisdiction, profile.Name,
        profile.Versions.OrderByDescending(v => v.Version).Select(ToDto).ToList());

    public static TaxProfileVersionDto ToDto(TaxProfileVersion version) => new(
        version.Id,
        version.Version,
        version.EffectiveFrom,
        version.Status.ToString(),
        version.PriceMode.ToString(),
        version.ShippingTaxable,
        version.Verification.State.ToString(),
        version.Verification.By,
        version.Verification.At,
        version.Verification.Note,
        version.RegistrationThreshold is { } threshold ? new MoneyDto(threshold.Amount, threshold.Currency) : null,
        version.Notes,
        version.Rates.OrderBy(r => r.Code, StringComparer.Ordinal)
            .Select(r => new TaxRateDto(r.Code, r.Name, r.BasisPoints, r.Category)).ToList());
}

// ============================================================================
// أسبابُ عدم الجمع، برموزٍ ثابتة تتفرّع عليها الواجهة (ADR-0017).
//
// وجودُ هذه القائمة هو ما يجعل «الضريبة صفر» جواباً لا صمتاً: التاجر يقرأ **لماذا** لا يُجمَع
// شيء، فيعرف أهو لم يختر ملفّاً، أم اختار ولم يفعّل، أم فعّل وملفُّه ينتظر محاسباً.
// ============================================================================
public static class TaxCollectionReasons
{
    public const string Collecting = "Collecting";
    public const string NoProfileSelected = "NoProfileSelected";
    public const string CollectionDisabled = "CollectionDisabled";
    public const string NoEffectiveVersion = "NoEffectiveVersion";
    public const string VersionNotVerified = "VersionNotVerified";
}
