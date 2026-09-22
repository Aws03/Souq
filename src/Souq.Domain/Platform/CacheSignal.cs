using Souq.Domain.Common;
using Souq.Domain.Exceptions;

namespace Souq.Domain.Platform;

// ============================================================================
// إشارةُ إبطالِ ذاكرةٍ مؤقتة تعبر نسخَ الخادم (C4، [ADR-0057](0057)): صفٌّ واحد لكل ذاكرة يحمل
// **عدّاد جيلٍ** يتزايد. جدولُ منصّةٍ عالميّ بلا متجر (الشكل C) — الإشارةُ تخصّ النسخَ لا المتاجر.
//
// ============================================================================
// **ولماذا عدّادٌ يُقرأ دورياً، لا رسالةٌ تُدفَع؟**
//
// لأنّ الدفعَ يحتاج وسيطاً، والوسيطُ غيرُ مقصودٍ صراحةً ([ExplicitNonGoals §3](../../../docs/02-ARCHITECTURE/ExplicitNonGoals.md))،
// والمخزنُ الموزَّع كذلك (§9). وما يُحتاج إليه هنا أبسطُ بكثير ممّا يقدّمه أيٌّ منهما: عددٌ صحيح
// يكبر، وكلُّ نسخةٍ تسأل «هل كبر منذ آخر مرّة؟» — وهو سؤالٌ يُجاب بصفٍّ واحد في جدولٍ من صفوفٍ
// معدودة، أرخصُ من أن يُحسب.
//
// **والذاكرتان اللتان يخدمهما هذا الجدول تعملان بالجيل أصلاً**: `TenantDirectoryCache` و
// `SessionStampCache` كلتاهما تُبطلان بقفزةِ عدّادٍ محلّيّ يدخل في مفتاح كل قيمة. فما يفعله هذا
// الجدول هو أن يجعل تلك القفزة **مشتركة** — لا آليةً جديدة تُضاف إلى آليةٍ قائمة.
// ============================================================================
//
// **والفجوةُ تبقى، وهي مقصودةٌ ومقيسة**: نسخةٌ أخرى تعرف بالتغيير خلال دورةِ الاستطلاع لا في
// اللحظة نفسها. وقبل هذا كانت تعرف بعد انتهاء المدّة — ستّين ثانيةً للمتاجر وثلاثين للأختام —
// فما جرى هو أنّ نافذةَ التقادم انكمشت من دقيقةٍ إلى ثوانٍ معدودة، لا أنّها أُلغيت. وإلغاؤها
// يحتاج قراءةً على كل طلب، وهو ثمنٌ لا يشتري شيئاً هنا.
// ============================================================================
public class CacheSignal : Entity
{
    public const int NameMaxLength = 60;

    // أسماءُ الذاكرات المُشار إليها. ثوابتُ لا إعداد: مَن يُبطل يعرف ما يُبطل.
    public const string TenantDirectory = "tenant-directory";
    public const string SessionStamps = "session-stamps";

    public static readonly IReadOnlyList<string> All = [TenantDirectory, SessionStamps];

    public string Name { get; private set; } = default!;

    // يتزايد ولا ينقص أبداً. كلُّ نسخةٍ تحتفظ بآخر قيمةٍ رأتها، وأيُّ زيادةٍ تعني «أبطِل محلّياً».
    // و`long` لا `int`: إبطالٌ في كل ثانية لمئة عام لا يقترب من حدّه، والالتفافُ حول الحدّ كان
    // سيجعل قيمةً أقدمَ تبدو أحدث.
    public long Version { get; private set; }

    private CacheSignal() { }

    public static CacheSignal For(string name)
    {
        var trimmed = name?.Trim() ?? "";
        if (trimmed.Length is 0 or > NameMaxLength)
            throw new InvalidLeaseException($"اسمُ إشارة الذاكرة مطلوب، حتى {NameMaxLength} حرفاً");
        if (!All.Contains(trimmed, StringComparer.Ordinal))
            throw new InvalidLeaseException($"إشارةُ ذاكرةٍ غير معروفة: {trimmed}");
        return new CacheSignal { Name = trimmed };
    }
}
