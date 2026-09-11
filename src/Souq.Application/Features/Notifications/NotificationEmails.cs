using Souq.Application.Common.Notifications;
using Souq.Application.Common.Tenancy;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Notifications;

// ============================================================================
// إرسال رسالة بهوية نطاقها (المرحلة 14): متجر السياق ⇒ اسمه المعروض بلغته الافتراضية، شعاره، ألوانه، وبريد تواصله ردّاً؛
// نطاق المنصّة ⇒ هوية المنصّة. الهوية تُقرأ من صفّ المتجر لحظة الإرسال: تعديلها يسري على الرسالة التالية. لا يُستدعى إلا
// من معالجي صندوق الصادر — مسار الطلب لا يعرف مزوّد البريد.
// ============================================================================
public sealed class NotificationEmails
{
    public const string PlatformName = "Souq";
    public const string PlatformCulture = "ar";
    private const string PlatformPrimary = "#0F3B3A";
    private const string PlatformOnPrimary = "#FAF7F1";

    private readonly ITenantRepository _tenants;
    private readonly ITenantContext _context;
    private readonly IEmailComposer _composer;
    private readonly IEmailSender _sender;

    public NotificationEmails(ITenantRepository tenants, ITenantContext context, IEmailComposer composer, IEmailSender sender)
    {
        _tenants = tenants; _context = context; _composer = composer; _sender = sender;
    }

    public async Task SendAsync(
        string to, EmailTemplate template, string origin, string? actionUrl, IReadOnlyDictionary<string, string> values,
        CancellationToken ct)
    {
        var (branding, culture) = await BrandingAsync(origin, ct);
        var email = _composer.Compose(new EmailContent(template, culture, branding, actionUrl, values));
        await _sender.SendAsync(new EmailMessage(
            to, email.Subject, email.HtmlBody, email.TextBody, branding.StoreName, branding.ContactEmail, template.ToString(), actionUrl), ct);
    }

    private async Task<(EmailBranding Branding, string Culture)> BrandingAsync(string origin, CancellationToken ct)
    {
        if (_context.Scope != TenantScope.Tenant)
            return (new EmailBranding(PlatformName, null, PlatformPrimary, PlatformOnPrimary, null), PlatformCulture);

        var tenant = await _tenants.GetByIdAsync(_context.RequireTenant().Id, ct)
            ?? throw new InvalidOperationException("متجر السياق غير موجود في القاعدة");
        var settings = tenant.Settings;
        var culture = tenant.DefaultCulture;
        var name = settings.DisplayName.TryGetValue(culture, out var display) && !string.IsNullOrWhiteSpace(display) ? display : tenant.Name;
        // الشعار مسار تحت بادئة ملفات المتجر (Tenant.SetBrandingAsset) — يُكمَّل بأصل الواجهة ليُعرض في عميل البريد.
        var logo = settings.Branding.LogoUrl is { Length: > 0 } path ? $"{origin.TrimEnd('/')}{path}" : null;
        return (new EmailBranding(name, logo, settings.Branding.Colors.Primary, settings.Branding.Colors.OnPrimary, settings.Contact.Email),
            culture);
    }
}
