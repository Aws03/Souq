using System.Text.RegularExpressions;
using Souq.Domain.Exceptions;

namespace Souq.Domain.Platform;

// ============================================================================
// Limit — "كم؟" مقابل الاستحقاق الذي هو "هل يجوز؟" (ADR-0047 §4: مفهومان لا يُخلطان). قيمة،
// لا كيان: اسمٌ وعدد غير سالب تحمله الخطة.
//
// **الاسم من كتالوج مغلق منذ C2** (LimitNames، ADR-0054). كان الشكل وحده يُتحقَّق منه في C1 كي لا
// يُجاب قرار المالك C-12 ضمناً؛ ولمّا وُجد الفرض صار الاسمُ المجهول وعداً لا يُنفَّذ — الحجّة
// كاملةً في LimitNames. والقيمة تبقى بلا كتالوج: **كم** يبقى سؤال المالك وحده.
//
// NormalizeName يبقى للشكل وحده لأن له مستعملاً لا يعرف العضوية: القراءة من القاعدة
// (PlanLimit في صفّ كُتب قبل أن يوجد الكتالوج) لا يجوز أن تُسقِط متجراً — التسامح في القراءة
// والتشدّد في الكتابة، كما في StoreModules.Parse.
// ============================================================================
public sealed partial record Limit
{
    public const int NameMinLength = 2;
    public const int NameMaxLength = 60;

    public string Name { get; }
    public int Value { get; }

    public Limit(string name, int value)
    {
        Name = LimitNames.Normalize(name);
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
