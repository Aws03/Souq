using Souq.Domain.Common;
using Souq.Domain.Exceptions;
using Souq.Domain.ValueObjects;

namespace Souq.Domain.Entities;

// ============================================================================
// ShippingMethod — طريقة شحن يعرّفها المتجر (المرحلة 12، ADR-0032): سعر ثابت بعملة المتجر، مجانية فوق حدّ اختياري (يُقاس
// بإجمالي السلة بعد الخصم)، دول تخدمها (بلا دول ⇒ كل مكان)، مدّة توصيل تقديرية، وناقل برابط تتبّع قالبه يحمل {number}.
// قواعد السعر والتوفّر هنا — خطّ التسعير يسأل الكيان ولا يكرّرها، والطلب يأخذ لقطته.
// ============================================================================
public class ShippingMethod : Entity, ITenantOwned
{
    public const int NameMaxLength = 100;
    public const int CarrierMaxLength = 100;
    public const int TrackingUrlMaxLength = 300;
    public const int CountriesMaxLength = 300;
    public const int MaxEstimateDays = 90;
    public const string TrackingNumberToken = "{number}";

    public int TenantId { get; private set; }
    public string Name { get; private set; } = default!;
    public Money Price { get; private set; } = default!;
    public decimal? FreeOverAmount { get; private set; }
    public string? Carrier { get; private set; }
    public string? TrackingUrlTemplate { get; private set; }
    public int? MinDays { get; private set; }
    public int? MaxDays { get; private set; }
    public string Countries { get; private set; } = "";   // رموز ISO بفواصل ("JO,SA") — فارغ ⇒ كل الدول
    public bool IsActive { get; private set; }
    public int SortOrder { get; private set; }

    private ShippingMethod() { }

    public ShippingMethod(string name, Money price, decimal? freeOverAmount, string? carrier, string? trackingUrlTemplate,
                          int? minDays, int? maxDays, IEnumerable<string>? countries, int sortOrder = 0)
    {
        Update(name, price, freeOverAmount, carrier, trackingUrlTemplate, minDays, maxDays, countries, sortOrder);
        IsActive = true;
    }

    public IReadOnlyList<string> CountryList => Countries.Length == 0 ? [] : Countries.Split(',');

    public Money? FreeOver => FreeOverAmount is decimal amount ? new Money(amount, Price.Currency) : null;

    // تعديل كامل: قيمة مرفوضة لا تغيّر شيئاً (التحقّق كله قبل أي إسناد).
    public void Update(string name, Money price, decimal? freeOverAmount, string? carrier, string? trackingUrlTemplate,
                       int? minDays, int? maxDays, IEnumerable<string>? countries, int sortOrder)
    {
        var trimmedName = name?.Trim();
        if (string.IsNullOrEmpty(trimmedName) || trimmedName.Length > NameMaxLength)
            throw Invalid($"اسم طريقة الشحن مطلوب (حتى {NameMaxLength} محرف)");
        ArgumentNullException.ThrowIfNull(price);
        if (freeOverAmount is decimal threshold)
        {
            if (threshold <= 0) throw Invalid("حدّ الشحن المجاني أكبر من صفر");
            _ = new Money(threshold, price.Currency);   // بخانات عملة السعر نفسها
        }
        if (minDays is < 0 || maxDays is < 0 || minDays > MaxEstimateDays || maxDays > MaxEstimateDays)
            throw Invalid($"مدّة التوصيل بين 0 و{MaxEstimateDays} يوماً");
        if ((minDays is null) != (maxDays is null) || minDays > maxDays)
            throw Invalid("مدّة التوصيل: أقلّها وأكثرها معاً، والأقلّ لا يتجاوز الأكثر");
        var trimmedCarrier = string.IsNullOrWhiteSpace(carrier) ? null : carrier.Trim();
        if (trimmedCarrier is { Length: > CarrierMaxLength })
            throw Invalid($"اسم الناقل حتى {CarrierMaxLength} محرف");
        var template = NormalizeTrackingTemplate(trackingUrlTemplate);
        var countryCodes = NormalizeCountries(countries);

        Name = trimmedName;
        Price = price;
        FreeOverAmount = freeOverAmount;
        Carrier = trimmedCarrier;
        TrackingUrlTemplate = template;
        MinDays = minDays;
        MaxDays = maxDays;
        Countries = countryCodes;
        SortOrder = sortOrder;
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;

    // تخدم هذا العنوان؟ بلا دول ⇒ كل مكان. بدول ⇒ العنوان يلزمه دولة معروفة منها (نصّ حرّ بلا دولة لا يكفي).
    public bool Serves(string? country) =>
        Countries.Length == 0 || (country is { Length: 2 } code && CountryList.Contains(code.ToUpperInvariant()));

    // سعر الشحن لسلة بهذا الإجمالي بعد الخصم: مجاني حين يبلغ الحدّ.
    public Money RateFor(Money goods)
    {
        if (goods.Currency != Price.Currency)
            throw Invalid("عملة السلة لا تطابق عملة طريقة الشحن");
        return FreeOverAmount is decimal threshold && goods.Amount >= threshold ? Money.Zero(Price.Currency) : Price;
    }

    // رابط تتبّع شحنة برقمها من قالب الناقل — الرقم مُرمَّز فلا يغيّر بنية الرابط.
    public static string? TrackingUrl(string? template, string? trackingNumber) =>
        string.IsNullOrWhiteSpace(template) || string.IsNullOrWhiteSpace(trackingNumber)
            ? null
            : template.Replace(TrackingNumberToken, Uri.EscapeDataString(trackingNumber.Trim()), StringComparison.Ordinal);

    private static string? NormalizeTrackingTemplate(string? template)
    {
        if (string.IsNullOrWhiteSpace(template)) return null;
        var trimmed = template.Trim();
        if (trimmed.Length > TrackingUrlMaxLength || !trimmed.Contains(TrackingNumberToken, StringComparison.Ordinal)
            || !Uri.TryCreate(trimmed.Replace(TrackingNumberToken, "0", StringComparison.Ordinal), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps)
            throw Invalid($"رابط التتبّع يبدأ بـ https:// ويحمل {TrackingNumberToken} مكان رقم الشحنة");
        return trimmed;
    }

    private static string NormalizeCountries(IEnumerable<string>? countries)
    {
        var codes = (countries ?? []).Select(c => c?.Trim().ToUpperInvariant() ?? "")
            .Where(c => c.Length > 0).Distinct().Order(StringComparer.Ordinal).ToList();
        if (codes.Any(c => c.Length != 2 || !c.All(char.IsAsciiLetterUpper)))
            throw Invalid("الدول برموز ISO من حرفين (JO، SA…)");
        var joined = string.Join(',', codes);
        if (joined.Length > CountriesMaxLength)
            throw Invalid("عدد الدول أكبر من المسموح");
        return joined;
    }

    private static InvalidShippingMethodException Invalid(string message) => new(message);
}
