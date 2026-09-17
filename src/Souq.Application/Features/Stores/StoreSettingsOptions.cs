using MediatR;
using Souq.Domain.Platform;

namespace Souq.Application.Features.Stores;

// ============================================================================
// ما يقبله محرّر إعدادات المتجر (GET /api/admin/store/settings/options): القوائم المعتمدة والحدود
// وعتبات التباين كما يفرضها Domain نفسه.
//
// لماذا نقطة لا ثوابت في الواجهة؟ لأن نسخة ثانية من القوائم تفترق يوماً: خطّ يُضاف هنا ولا يظهر
// في المحرّر، أو حدٌّ يُشدَّد هنا فتسمح الواجهة بما يرفضه الخادم. الواجهة تفحص مسبقاً لتقول للتاجر
// *ما* الخطأ بلغته (رسائل Domain عربية ورمزها واحد لكل قواعد الإعدادات)، والخادم يبقى الحَكَم.
// لا بيانات متجر هنا — قيم المنصّة كلها، لكنها خلف صلاحية الإعدادات لأن لا أحد غير المحرّر يحتاجها.
// ============================================================================
public sealed record SocialNetworkOptionDto(string Network, IReadOnlyList<string> Domains);

public sealed record StoreSettingsLimitsDto(
    int DisplayName, int Announcement, int SeoTitle, int SeoDescription, int Address,
    int SocialLinks, int SocialUrl, int TimeZone, long BrandingFileBytes);

public sealed record StoreContrastRulesDto(double Text, double Ui);

public sealed record StoreSettingsOptionsDto(
    IReadOnlyList<string> Cultures, IReadOnlyList<string> Typography, IReadOnlyList<string> ThemePresets,
    IReadOnlyList<string> ThemeModes, IReadOnlyList<string> OpeningStyles,
    IReadOnlyList<SocialNetworkOptionDto> SocialNetworks, StoreSettingsLimitsDto Limits, StoreContrastRulesDto Contrast);

public record GetStoreSettingsOptionsQuery : IRequest<StoreSettingsOptionsDto>;

public class GetStoreSettingsOptionsHandler : IRequestHandler<GetStoreSettingsOptionsQuery, StoreSettingsOptionsDto>
{
    // عامّة للقراءة: محرّر المنصّة يعرض القوائم نفسها (GetProvisioningOptionsQuery) — نسخة واحدة لا اثنتان.
    public static readonly StoreSettingsOptionsDto Options = new(
        Tenant.SupportedCultures, BrandPresets.Typography, BrandPresets.Themes, BrandPresets.ThemeModes,
        BrandPresets.OpeningStyles,
        SocialLink.Networks.Select(n => new SocialNetworkOptionDto(n.Key, n.Value)).ToList(),
        new StoreSettingsLimitsDto(
            StoreSettings.DisplayNameMaxLength, StoreSettings.AnnouncementMaxLength,
            SeoSettings.TitleMaxLength, SeoSettings.DescriptionMaxLength, StoreContact.AddressMaxLength,
            StoreSettings.MaxSocialLinks, SocialLink.UrlMaxLength, Tenant.TimeZoneMaxLength, BrandingFiles.MaxBytes),
        new StoreContrastRulesDto(BrandColors.MinimumTextContrast, BrandColors.MinimumUiContrast));

    public Task<StoreSettingsOptionsDto> Handle(GetStoreSettingsOptionsQuery query, CancellationToken ct) =>
        Task.FromResult(Options);
}
