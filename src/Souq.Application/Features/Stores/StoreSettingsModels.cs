using FluentValidation;
using Souq.Domain.Platform;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Features.Stores;

// ============================================================================
// إعدادات واجهة المتجر (وحدة Platform — WhiteLabel.md §2): شكل واحد للمدخل ولمخرجاته في مسارَي التعديل —
// المنصّة عند التجهيز، ومدير المتجر بعد التسليم. العملة والنطاقات والوحدات والحالة ليست هنا عمداً: قرارات
// المنصّة وحدها (عقود، فوترة، سلامة المنصّة).
// ============================================================================

public sealed record BrandColorsInput(string Primary, string Secondary, string Accent, string Background, string Text);
public sealed record SocialLinkInput(string Network, string Url);
public sealed record StoreLocaleInput(string DefaultCulture, IReadOnlyList<string> EnabledCultures, string TimeZone);
public sealed record StoreOpeningInput(bool Enabled, string? Style = null);
public sealed record StoreBrandingInput(
    BrandColorsInput Colors, string Typography, string ThemePreset,
    string? ThemeMode = null, StoreOpeningInput? Opening = null);
public sealed record StoreContactInput(string? Email, string? Phone, IReadOnlyDictionary<string, string?>? Address);
public sealed record StoreSeoInput(IReadOnlyDictionary<string, string?>? Title, IReadOnlyDictionary<string, string?>? Description);

public sealed record StoreSettingsInput(
    IReadOnlyDictionary<string, string?>? DisplayName, StoreLocaleInput Locale, StoreBrandingInput Branding,
    StoreContactInput? Contact, IReadOnlyList<SocialLinkInput>? Social, StoreSeoInput? Seo,
    IReadOnlyDictionary<string, string?>? Announcement,
    // روابط السياسات: النوع ⇒ عنوانه (TD-42). غائبة ⇒ تُمسح كبقيّة هذا العقد — الحفظ يستبدل كل ما
    // فيه، والواجهة ترسله كاملاً دائماً.
    IReadOnlyDictionary<string, string?>? Policies = null,
    // أقسام الرئيسية بترتيبها (C8). **غائبةٌ ⇒ تبقى كما هي**، على خلاف بقيّة هذا العقد الذي
    // يستبدل ما فيه: عميلٌ أقدم لا يعرف الحقل كان سيُعيد كلَّ متجرٍ يحفظ منه إلى الترتيب
    // الافتراضي بلا أن يطلب أحدٌ ذلك — وتخطيطٌ يُمحى بحفظِ حقلٍ آخر عطبٌ لا عقد.
    IReadOnlyList<StoreSectionInput>? Sections = null);

public sealed record StoreSectionInput(string Type, bool Enabled);
public sealed record StoreSectionDto(string Type, bool Enabled);

public sealed record BrandColorsDto(
    string Primary, string Secondary, string Accent, string Background, string Text, string OnPrimary, string OnAccent);
public sealed record StoreOpeningDto(bool Enabled, string Style);
public sealed record StoreBrandingDto(
    BrandColorsDto Colors, string Typography, string ThemePreset, string ThemeMode, StoreOpeningDto Opening,
    string? LogoUrl, string? FaviconUrl, string? SocialImageUrl);
public sealed record StoreContactDto(string? Email, string? Phone, IReadOnlyDictionary<string, string> Address);
public sealed record SocialLinkDto(string Network, string Url);
public sealed record StoreSeoDto(IReadOnlyDictionary<string, string> Title, IReadOnlyDictionary<string, string> Description);
public sealed record StoreLocaleDto(
    string DefaultCulture, IReadOnlyList<string> EnabledCultures, string TimeZone, string Currency, int CurrencyDecimals);

public sealed record StoreSettingsDto(
    IReadOnlyDictionary<string, string> DisplayName, StoreLocaleDto Locale, StoreBrandingDto Branding,
    StoreContactDto Contact, IReadOnlyList<SocialLinkDto> Social, StoreSeoDto Seo,
    IReadOnlyDictionary<string, string> Announcement,
    // روابط السياسات المضبوطة وحدها (TD-42): النوع غير المضبوط غائب لا فارغ، فالتذييل يرسم ما يجد.
    IReadOnlyDictionary<string, string> Policies,
    // الأقسام كاملةً — المُطفأُ منها أيضاً — كي يعرف المحرّرُ ما يمكن تشغيلُه، و`EnabledSections`
    // ما تعرضه الواجهةُ فعلاً بترتيبه. الاثنان معاً لأنّ للشاشتين سؤالين مختلفين.
    IReadOnlyList<StoreSectionDto> Sections,
    IReadOnlyList<string> EnabledSections);

// إعداد الواجهة العام (GET /api/storefront/config): عرض فقط — لا أسرار، ولا بريد إداري، ولا معرّفات داخلية.
public sealed record StorefrontConfigDto(
    string Slug, string Name, string Status, StoreSettingsDto Settings, IReadOnlyList<string> Modules);

public static class StoreSettingsMapper
{
    public static StoreSettingsDto ToDto(Tenant tenant)
    {
        var settings = tenant.Settings;
        var branding = settings.Branding;
        var colors = branding.Colors;
        return new StoreSettingsDto(
            settings.DisplayName,
            new StoreLocaleDto(tenant.DefaultCulture, settings.EnabledCultures, tenant.TimeZone, tenant.Currency,
                CurrencyInfo.MinorUnits(tenant.Currency)),
            new StoreBrandingDto(
                new BrandColorsDto(colors.Primary, colors.Secondary, colors.Accent, colors.Background, colors.Text,
                    colors.OnPrimary, colors.OnAccent),
                branding.Typography, branding.ThemePreset, branding.ThemeMode,
                new StoreOpeningDto(branding.Opening.Enabled, branding.Opening.Style),
                branding.LogoUrl, branding.FaviconUrl, branding.SocialImageUrl),
            new StoreContactDto(settings.Contact.Email, settings.Contact.Phone, settings.Contact.Address),
            settings.Social.Select(l => new SocialLinkDto(l.Network, l.Url)).ToList(),
            new StoreSeoDto(settings.Seo.Title, settings.Seo.Description),
            settings.Announcement,
            settings.Policies.Urls,
            settings.Sections.Items.Select(i => new StoreSectionDto(i.Type, i.Enabled)).ToList(),
            // ما يُرسَم فعلاً، محسوباً في الخادم: الواجهةُ تعرض ولا تقرّر (FrontendGuide).
            settings.Sections.EnabledTypes);
    }

    public static StorefrontConfigDto ToStorefront(Tenant tenant) => new(
        tenant.Slug, tenant.Name, tenant.Status.ToString(), ToDto(tenant),
        tenant.Modules.Order(StringComparer.Ordinal).ToList());
}

// يطبّق المدخل على التجمّع بقواعده (Domain) — القيم تُبنى بمصانعها المُتحقِّقة، ثم يستبدلها Tenant كاملةً.
public static class StoreSettingsEditor
{
    public static void Apply(Tenant tenant, StoreSettingsInput input)
    {
        tenant.SetLocale(input.Locale.DefaultCulture, input.Locale.TimeZone);
        tenant.SetEnabledCultures(input.Locale.EnabledCultures);

        var colors = input.Branding.Colors;
        tenant.UpdateBranding(
            BrandColors.Create(colors.Primary, colors.Secondary, colors.Accent, colors.Background, colors.Text),
            input.Branding.Typography, input.Branding.ThemePreset, input.Branding.ThemeMode,
            input.Branding.Opening is { } opening ? StoreOpening.Create(opening.Enabled, opening.Style) : null);

        tenant.UpdateStorefront(
            input.DisplayName,
            StoreContact.Create(input.Contact?.Email, input.Contact?.Phone, input.Contact?.Address),
            (input.Social ?? []).Select(l => SocialLink.Create(l.Network, l.Url)).ToList(),
            SeoSettings.Create(input.Seo?.Title, input.Seo?.Description),
            input.Announcement,
            StorePolicyLinks.Create(input.Policies));

        tenant.UpdateSections(input.Sections is null
            ? null
            : StoreSections.Create([.. input.Sections.Select(x => new StoreSection(x.Type, x.Enabled))]));
    }
}

// الشكل فقط (موجود، غير فارغ)؛ قواعد القيم في Domain وتصل 422 برسالتها.
public sealed class StoreSettingsInputValidator : AbstractValidator<StoreSettingsInput>
{
    public StoreSettingsInputValidator()
    {
        RuleFor(x => x.Locale).NotNull();
        RuleFor(x => x.Locale.EnabledCultures).NotEmpty().When(x => x.Locale is not null);
        RuleFor(x => x.Branding).NotNull();
        RuleFor(x => x.Branding.Colors).NotNull().When(x => x.Branding is not null);
        RuleFor(x => x.Social!.Count).LessThanOrEqualTo(StoreSettings.MaxSocialLinks).When(x => x.Social is not null);
        RuleFor(x => x.Policies!.Count).LessThanOrEqualTo(StorePolicyLinks.Kinds.Count).When(x => x.Policies is not null);
    }
}
