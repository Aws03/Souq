using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Souq.Application.Features.Baskets;

// ============================================================================
// عقود السلة (المرحلة 8، ADR-0028). الأسعار والمجاميع من خطّ التسعير الواحد لحظة الطلب — لا شيء منها مخزَّن. Available
// من Inventory للعرض فقط (لا حجز)، وReadyForCheckout يلخّص ما سيقبله الدفع.
// ============================================================================
public sealed record BasketText(string Name);

// VariantLabel (V3): وصف المتغيّر الحيّ من خياراته ("M / أحمر") بلغة المتجر، وVariantLabels بكل لغاته (الواجهة تعرض
// لغتها كما تفعل بالاسم) — كلاهما null/فارغ لمنتج بلا خيارات. حيٌّ لا لقطة: السلة تعرض الكتالوج الآن، ولقطة الشراء
// تُجمَّد على سطر الطلب وحده.
public sealed record BasketLineDto(
    int ProductId, int VariantId, string Name, IReadOnlyDictionary<string, BasketText> Translations, string? ImageUrl,
    decimal UnitPrice, int Quantity, decimal LineTotal, bool Sellable, int Available, string? VariantLabel = null,
    IReadOnlyDictionary<string, string>? VariantLabels = null);

public sealed record BasketCouponDto(string Code, bool Applied, string? ErrorCode, string? Message);

// ShippingMethods (المرحلة 12): الطرق المتاحة لعنوان التسعير بتكلفتها ومدّتها، والمختارة، وهل يلزم اختيار (للمتجر طرق)،
// ومشكلة الاختيار نتيجةً لا فشلاً (ShippingMethodRequired، ShippingMethodUnavailable، ShippingNotAvailable).
public sealed record ShippingOptionDto(int MethodId, string Name, decimal Cost, string? Carrier, int? MinDays, int? MaxDays);

public sealed record BasketShippingDto(
    IReadOnlyList<ShippingOptionDto> Options, int? SelectedMethodId, bool Required, string? ErrorCode, string? Message);

public sealed record BasketDto(
    IReadOnlyList<BasketLineDto> Lines, int ItemCount, string Currency,
    decimal Subtotal, decimal Discount, decimal Shipping, decimal Tax, decimal Total,
    BasketCouponDto? Coupon, bool ReadyForCheckout, DateTime? ExpiresAt, BasketShippingDto? ShippingMethods = null);

// ما يفعله الـ API بملف تعريف ارتباط الزائر بعد الطلب. الرمز لا يغادر هذه النتيجة إلا إلى ملف التعريف (HttpOnly).
public enum GuestCookieAction { None, Set, Clear }

public sealed record BasketResult(BasketDto Basket, GuestCookieAction Cookie, string? GuestToken = null, DateTime? GuestExpiresAt = null);

// إعدادات السلال (Basket:*، يتحقّق منها Infrastructure عند الإقلاع): عمر سلة الزائر وسلة العميل منذ آخر تعديل، ودورة
// منسّق حذف المنتهية بالدقائق (0 = معطّل؛ الاختبارات ترسل أمر الحذف مباشرة).
public sealed class BasketSettings
{
    public int GuestLifetimeDays { get; set; } = 30;
    public int CustomerLifetimeDays { get; set; } = 180;
    public int CleanupIntervalMinutes { get; set; } = 60;

    public TimeSpan LifetimeFor(bool guest) => TimeSpan.FromDays(guest ? GuestLifetimeDays : CustomerLifetimeDays);
}

// رمز سلة الزائر: 256 بت عشوائية بترميز Base64Url في ملف تعريف ارتباط HttpOnly؛ القاعدة تحفظ بصمته SHA-256 وحدها.
public static class GuestBasketTokens
{
    public const int TokenLength = 43;   // 32 بايت بـ Base64Url بلا حشو

    public static string New() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    public static bool IsWellFormed(string? token) =>
        token is { Length: TokenLength } && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    public static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(token)));
}
