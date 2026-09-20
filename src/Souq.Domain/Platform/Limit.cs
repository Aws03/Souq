using System.Text.RegularExpressions;
using Souq.Domain.Exceptions;

namespace Souq.Domain.Platform;

// ============================================================================
// Limit — "كم؟" مقابل الاستحقاق الذي هو "هل يجوز؟" (ADR-0047 §4: مفهومان لا يُخلطان). قيمة،
// لا كيان: اسمٌ وعدد غير سالب تحمله الخطة.
//
// **لا كتالوج لأسماء الحدود هنا عمداً.** ما هي الشرائح وما حدودها وهل كل حدّ صلب أم ليّن أم قابل
// للتجاوز بفوترة — سؤال المالك C-12، وسنّ قائمة أسماء هنا كان سيجيبه ضمناً. فالمُتحقَّق منه هو
// **شكل** الاسم لا عضويّته، وفرضُ الحدود كلّه في C2 (ADR-0049) لا هنا.
// ============================================================================
public sealed partial record Limit
{
    public const int NameMinLength = 2;
    public const int NameMaxLength = 60;

    public string Name { get; }
    public int Value { get; }

    public Limit(string name, int value)
    {
        Name = NormalizeName(name);
        Value = value >= 0 ? value : throw new InvalidPlanException("قيمة الحدّ لا تكون سالبة");
    }

    public static string NormalizeName(string? name)
    {
        var normalized = name?.Trim().ToLowerInvariant() ?? "";
        if (normalized.Length < NameMinLength || normalized.Length > NameMaxLength || !NamePattern().IsMatch(normalized))
            throw new InvalidPlanException($"اسم الحدّ يقبل أحرفاً لاتينية صغيرة وأرقاماً ونقاطاً وشرطات ({NameMinLength}–{NameMaxLength} حرفاً)");
        return normalized;
    }

    [GeneratedRegex("^[a-z0-9]+(?:[.-][a-z0-9]+)*$")]
    private static partial Regex NamePattern();
}
