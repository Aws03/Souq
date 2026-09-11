namespace Souq.Domain.ValueObjects;

// عدد الخانات العشرية (الوحدة الصغرى) لكل عملة حسب ISO 4217. الافتراضي 2؛ هنا
// الاستثناءات فقط — مصدر واحد للحقيقة يستخدمه Money ومحوّلات بوّابات الدفع.
public static class CurrencyInfo
{
    private const int DefaultMinorUnits = 2;

    private static readonly Dictionary<string, int> Exceptions = new(StringComparer.Ordinal)
    {
        // ثلاث خانات (الدينار الأردني بالفلس، إلخ)
        ["BHD"] = 3, ["IQD"] = 3, ["JOD"] = 3, ["KWD"] = 3, ["LYD"] = 3, ["OMR"] = 3, ["TND"] = 3,
        // بلا خانات عشرية
        ["BIF"] = 0, ["CLP"] = 0, ["DJF"] = 0, ["GNF"] = 0, ["ISK"] = 0, ["JPY"] = 0, ["KMF"] = 0,
        ["KRW"] = 0, ["PYG"] = 0, ["RWF"] = 0, ["UGX"] = 0, ["VND"] = 0, ["VUV"] = 0,
        ["XAF"] = 0, ["XOF"] = 0, ["XPF"] = 0,
        // أربع خانات (وحدات حسابية نادرة)
        ["CLF"] = 4, ["UYW"] = 4,
    };

    public static int MinorUnits(string currencyCode) =>
        Exceptions.TryGetValue(currencyCode.ToUpperInvariant(), out var units) ? units : DefaultMinorUnits;
}
