using System.Text.RegularExpressions;
using Souq.Domain.Exceptions;

namespace Souq.Domain.ValueObjects;

// ============================================================================
// عنوان بريدي منظَّم (المرحلة 7): المستلم وهاتفه، الدولة (ISO 3166-1 من حرفين)، المدينة، المنطقة، سطرا العنوان،
// والرمز البريدي. كائن قيمة ثابت يُطبَّع ويُتحقَّق منه عند الإنشاء. الطلب يحفظ لقطته النصّية (ToSingleLine) حتى تُهيكَل
// عناوين الطلب نفسها في المرحلة 9 — تعديل العنوان في الدفتر لاحقاً لا يغيّر طلباً سابقاً.
// ============================================================================
public sealed partial record PostalAddress
{
    public const int NameMaxLength = 150;
    public const int PhoneMaxLength = 20;
    public const int CityMaxLength = 100;
    public const int RegionMaxLength = 100;
    public const int LineMaxLength = 200;
    public const int PostalCodeMaxLength = 20;

    public string RecipientName { get; }
    public string Phone { get; }
    public string Country { get; }
    public string City { get; }
    public string? Region { get; }
    public string Line1 { get; }
    public string? Line2 { get; }
    public string? PostalCode { get; }

    public PostalAddress(
        string recipientName, string phone, string country, string city, string line1,
        string? region = null, string? line2 = null, string? postalCode = null)
    {
        RecipientName = Required(recipientName, NameMaxLength, "اسم المستلم");
        Phone = NormalizePhone(phone) ?? throw new InvalidCustomerDataException("هاتف المستلم مطلوب");
        var countryCode = country?.Trim().ToUpperInvariant() ?? "";
        Country = CountryPattern().IsMatch(countryCode)
            ? countryCode
            : throw new InvalidCustomerDataException("رمز الدولة حرفان لاتينيان (ISO 3166-1)، مثال: JO");
        City = Required(city, CityMaxLength, "المدينة");
        Line1 = Required(line1, LineMaxLength, "العنوان");
        Region = Optional(region, RegionMaxLength, "المنطقة");
        Line2 = Optional(line2, LineMaxLength, "تفاصيل العنوان");
        PostalCode = Optional(postalCode, PostalCodeMaxLength, "الرمز البريدي");
    }

    // لقطة سطر الشحن في الطلب — مقصوصة لحدّ عمود الطلب.
    public string ToSingleLine(int maxLength)
    {
        var line = string.Join("، ", new[] { RecipientName, Phone, Line1, Line2, City, Region, PostalCode, Country }
            .Where(part => !string.IsNullOrWhiteSpace(part)));
        return line.Length <= maxLength ? line : line[..maxLength];
    }

    // أرقام ومسافات و+ و- وأقواس، بين 6 و20 — لا نفرض صيغة دولة بعينها (المتاجر في بلدان مختلفة).
    public static string? NormalizePhone(string? phone)
    {
        var trimmed = phone?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        return PhonePattern().IsMatch(trimmed)
            ? trimmed
            : throw new InvalidCustomerDataException($"رقم هاتف غير صالح (أرقام و+ و- ومسافات، حتى {PhoneMaxLength})");
    }

    private static string Required(string? value, int max, string field) =>
        Optional(value, max, field) ?? throw new InvalidCustomerDataException($"{field} مطلوب");

    private static string? Optional(string? value, int max, string field)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        return trimmed.Length <= max ? trimmed : throw new InvalidCustomerDataException($"{field} يتجاوز {max} حرفاً");
    }

    [GeneratedRegex("^[A-Z]{2}$")]
    private static partial Regex CountryPattern();

    [GeneratedRegex(@"^\+?[0-9][0-9 ()\-]{5,19}$")]
    private static partial Regex PhonePattern();
}
