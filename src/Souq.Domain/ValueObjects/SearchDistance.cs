namespace Souq.Domain.ValueObjects;

// ============================================================================
// مسافة تحرير محدودة بين كلمتين مطبَّعتين (M3، ADR-0042) — أساس استرجاع الأخطاء المطبعية.
//
// الخيار: Damerau–Levenshtein بمحاذاة السلاسل المثلى (OSA) — أي أنّ تبديل حرفين متجاورين خطوة واحدة لا اثنتين.
// هذا مقصود: أشيع خطأ على لوحة مفاتيح هو تبديل جارَين ("مكسنة" عن "مكنسة")، ولو حُسب خطوتين لخرج عن أي حدّ معقول.
//
// **حتمية لا احتمالية:** الدالّة خالصة، ونتيجتها لا تعتمد على بيانات مدرَّبة ولا على خدمة خارجية ولا على عشوائية —
// وهو شرط M3 الصريح: الاسترجاع يجب أن يكون قابلاً للتفسير والاختبار، لا تخميناً من نموذج.
// ============================================================================
public static class SearchDistance
{
    // أبعد من هذا لم يكن خطأً مطبعياً بل كلمة أخرى: "مكلسة"→"مكنسة" مسافتها 1، و"مكنسه"→"مكنسة" صفر بعد التطبيع.
    public const int MaxTypoDistance = 2;

    // يتجاوز الحدّ ⇒ Beyond: القيمة تعني "ليست مرشَّحاً"، ولا تُقارَن كمسافة حقيقية.
    public const int Beyond = int.MaxValue;

    // ============================================================================
    // المسافة بين a و b إن كانت ≤ maxDistance، وإلا Beyond.
    //
    // **تُقصّ كل خلية عند maxDistance + 1 بدل حسابها كاملة.** هذا سليم للسؤال المطروح وحده ("أقرب من الحدّ؟"):
    // القصّ يرفع القيم ولا يخفضها، وكل خلية قيمتها الحقيقية ≤ الحدّ تبقى دقيقة — لأنّ أي مسار يمرّ بخلية مقصوصة
    // ينتج ≥ الحدّ + 1 فيفشل الفحص الأخير أصلاً. وبه لا يحتاج الكود خروجاً مبكّراً على مستوى الصفّ: الخروج المبكّر
    // الشائع (كل الصفّ فوق الحدّ ⇒ توقّف) **غير سليم مع OSA** لأنّ خطوة تبديل الجارَين تقرأ الصفّ قبل السابق،
    // فقد تعود قيمة صغيرة من صفّ ظُنّ أنّه استُنفد. القصّ يعطي الأداء نفسه بلا هذا الفخّ.
    // ============================================================================
    public static int Between(string? a, string? b, int maxDistance = MaxTypoDistance)
    {
        var first = a ?? "";
        var second = b ?? "";

        if (string.Equals(first, second, StringComparison.Ordinal)) return 0;
        if (maxDistance <= 0) return Beyond;

        // فرق الطول وحده حدٌّ أدنى للمسافة: كلمتان يفرق طولهما 3 لا تكونان خطأً مطبعياً بحدّ 2 — بلا أي حساب.
        if (Math.Abs(first.Length - second.Length) > maxDistance) return Beyond;
        if (first.Length == 0 || second.Length == 0)
        {
            var onlyLength = Math.Max(first.Length, second.Length);
            return onlyLength <= maxDistance ? onlyLength : Beyond;
        }

        var cap = maxDistance + 1;

        // ثلاثة صفوف متعاقبة تكفي: الصفّ الحالي يحتاج سابقه (حذف/إدراج/إبدال) وسابقَ سابقه (تبديل الجارَين).
        var twoRowsBack = new int[second.Length + 1];
        var previousRow = new int[second.Length + 1];
        var currentRow = new int[second.Length + 1];

        for (var column = 0; column <= second.Length; column++) previousRow[column] = Math.Min(column, cap);

        for (var row = 1; row <= first.Length; row++)
        {
            currentRow[0] = Math.Min(row, cap);

            for (var column = 1; column <= second.Length; column++)
            {
                var substitutionCost = first[row - 1] == second[column - 1] ? 0 : 1;
                var value = Math.Min(
                    Math.Min(previousRow[column] + 1, currentRow[column - 1] + 1),
                    previousRow[column - 1] + substitutionCost);

                if (row > 1 && column > 1
                    && first[row - 1] == second[column - 2]
                    && first[row - 2] == second[column - 1])
                {
                    value = Math.Min(value, twoRowsBack[column - 2] + 1);
                }

                currentRow[column] = Math.Min(value, cap);
            }

            (twoRowsBack, previousRow, currentRow) = (previousRow, currentRow, twoRowsBack);
        }

        var distance = previousRow[second.Length];
        return distance <= maxDistance ? distance : Beyond;
    }

    // هل الكلمتان متقاربتان بما يجعل إحداهما تصحيحاً للأخرى؟
    public static bool IsTypoOf(string? candidate, string? query, int maxDistance = MaxTypoDistance) =>
        Between(candidate, query, maxDistance) != Beyond;
}
