using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Souq.Domain.Platform;

namespace Souq.Infrastructure.Persistence.Configurations;

// ============================================================================
// إعدادات الواجهة ⇄ مستند JSON في عمود واحد (Tenants.Settings). مستند تخزين خاص بالبنية التحتية — لا سمات تسلسل
// في Domain — ويُعاد البناء بمُنشئات Domain الداخلية بلا تحقّق: قاعدة شُدّدت لاحقاً لا تُسقط متجراً حُفظت
// إعداداته قبلها. خانة ناقصة (إصدار أقدم من المستند) ⇒ قيمتها الافتراضية.
// ============================================================================
internal static class StoreSettingsJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    // EF لا يمرّر NULL للمحوّل (العمود الفارغ يبقى null في الحقل)، فالقيمة هنا دائماً موجودة.
    public static readonly ValueConverter<StoreSettings?, string> Converter = new(
        settings => Serialize(settings!), json => Deserialize(json));

    // القيمة ثابتة (Immutable): اللقطة هي الكائن نفسه، والمساواة بالمستند المسلسَل.
    public static readonly ValueComparer<StoreSettings?> Comparer = new(
        (a, b) => Json(a) == Json(b), settings => Json(settings).GetHashCode(), settings => settings);

    private static string Json(StoreSettings? settings) => settings is null ? "" : Serialize(settings);

    internal static string Serialize(StoreSettings s)
    {
        var colors = s.Branding.Colors;
        return JsonSerializer.Serialize(new SettingsDocument(
            new(s.DisplayName), [.. s.EnabledCultures],
            new BrandingDocument(
                new ColorsDocument(colors.Primary, colors.Secondary, colors.Accent, colors.Background, colors.Text),
                s.Branding.Typography, s.Branding.ThemePreset, s.Branding.LogoUrl, s.Branding.FaviconUrl,
                s.Branding.SocialImageUrl, s.Branding.ThemeMode,
                new OpeningDocument(s.Branding.Opening.Enabled, s.Branding.Opening.Style)),
            new ContactDocument(s.Contact.Email, s.Contact.Phone, new(s.Contact.Address)),
            [.. s.Social.Select(l => new SocialDocument(l.Network, l.Url))],
            new SeoDocument(new(s.Seo.Title), new(s.Seo.Description)),
            new(s.Announcement),
            new(s.Policies.Urls),
            [.. s.Sections.Items.Select(i => new SectionDocument(i.Type, i.Enabled))]), Options);
    }

    internal static StoreSettings Deserialize(string json)
    {
        var document = JsonSerializer.Deserialize<SettingsDocument>(json, Options)
            ?? throw new InvalidOperationException("مستند إعدادات متجر فارغ");
        var branding = document.Branding;
        var colors = branding?.Colors is { } c
            ? new BrandColors(c.Primary, c.Secondary, c.Accent, c.Background, c.Text)
            : BrandColors.Neutral;

        return new StoreSettings(
            document.DisplayName ?? new(),
            document.EnabledCultures ?? [],
            new StoreBranding(colors, branding?.Typography ?? BrandPresets.DefaultTypography,
                branding?.ThemePreset ?? BrandPresets.DefaultTheme, branding?.LogoUrl, branding?.FaviconUrl,
                branding?.SocialImageUrl, branding?.ThemeMode,
                // مستند أقدم بلا هذا الحقل ⇒ الافتراضي (معطّل)، لا كشف يظهر فجأةً لمتجر لم يطلبه.
                branding?.Opening is { } opening
                    ? new StoreOpening(opening.Enabled, opening.Style ?? BrandPresets.DefaultOpeningStyle)
                    : StoreOpening.Disabled),
            new StoreContact(document.Contact?.Email, document.Contact?.Phone, document.Contact?.Address ?? new()),
            (document.Social ?? []).Select(l => new SocialLink(l.Network, l.Url)).ToList(),
            new SeoSettings(document.Seo?.Title ?? new(), document.Seo?.Description ?? new()),
            document.Announcement ?? new(),
            // مستند أقدم بلا هذا الحقل ⇒ لا روابط سياسات، لا رابط يظهر في تذييل متجر لم يضبطه.
            document.Policies is { Count: > 0 } policies ? new StorePolicyLinks(policies) : StorePolicyLinks.Empty,
            // مستند أقدم بلا هذا الحقل ⇒ الترتيب الافتراضي كاملاً، أي رئيسيةُ المتجر كما هي اليوم.
            // ولا يمرّ بـ `Create`: ذاك يُلحق ما لم يُذكر **مُطفأً**، وهو الصحيح لمتجرٍ ضبط أقسامه
            // ثمّ أُضيف نوعٌ جديد — أمّا مستندٌ لم يُكتب فيه شيءٌ قطّ فلم يُطفئ أحدٌ فيه شيئاً.
            document.Sections is { Count: > 0 } sections
                ? new StoreSections([.. sections.Select(x => new StoreSection(x.Type, x.Enabled))])
                : StoreSections.Default);
    }

    internal sealed record SettingsDocument(
        Dictionary<string, string>? DisplayName, List<string>? EnabledCultures, BrandingDocument? Branding,
        ContactDocument? Contact, List<SocialDocument>? Social, SeoDocument? Seo, Dictionary<string, string>? Announcement,
        Dictionary<string, string>? Policies = null, List<SectionDocument>? Sections = null);

    internal sealed record SectionDocument(string Type, bool Enabled);

    internal sealed record BrandingDocument(
        ColorsDocument? Colors, string? Typography, string? ThemePreset, string? LogoUrl, string? FaviconUrl,
        string? SocialImageUrl, string? ThemeMode = null, OpeningDocument? Opening = null);

    internal sealed record OpeningDocument(bool Enabled, string? Style);

    internal sealed record ColorsDocument(string Primary, string Secondary, string Accent, string Background, string Text);

    internal sealed record ContactDocument(string? Email, string? Phone, Dictionary<string, string>? Address);

    internal sealed record SocialDocument(string Network, string Url);

    internal sealed record SeoDocument(Dictionary<string, string>? Title, Dictionary<string, string>? Description);
}
