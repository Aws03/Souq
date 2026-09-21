using Souq.Domain.Common;
using Souq.Domain.Interfaces;

namespace Souq.Domain.Entities;

// ============================================================================
// إلى أيّ يومٍ جُمِّعت أحداثُ هذا المتجر — صفٌّ واحد لكل متجر.
//
// **هذا الصفّ هو ما يجعل «التجميع قبل المسح» قاعدةً يفرضها الكود لا نصيحةً في وثيقة** ([ADR-0050](0050) §6).
// المسح لا يمسّ يوماً لم يُجمَّع، فما يُفقَد بانقضاء مدّة الحفظ هو التفصيل لا التاريخ. وبلا علامةٍ
// كهذه كان لا بدّ من استنتاج «هل جُمِّع هذا اليوم؟» من وجود صفوف تجميعٍ له — وهو استنتاج خاطئ:
// يومٌ بلا أحداث لا يُنتج صفَّ تجميعٍ واحداً، فيبدو أبداً كأنه لم يُجمَّع، فلا يُمسح شيءٌ بعده أبداً.
// ============================================================================
public class AnalyticsRollupState : Entity, ITenantOwned
{
    public int TenantId { get; private set; }

    // آخرُ يومٍ **مكتمل** جُمِّع. null ⇒ لم يُجمَّع شيء بعد ⇒ لا يُمسح شيء.
    public DateTime? RolledUpThroughDay { get; private set; }

    public DateTime? LastRunAt { get; private set; }

    private AnalyticsRollupState() { }

    public static AnalyticsRollupState Empty() => new();

    // يتقدّم ولا يتراجع: إعادةُ تجميع يومٍ قديم (تصحيحاً) لا تُعيد فتح ما بعده للمسح.
    public void Advance(DateTime day, DateTime utcNow)
    {
        if (RolledUpThroughDay is null || day.Date > RolledUpThroughDay.Value)
            RolledUpThroughDay = day.Date;
        LastRunAt = utcNow;
    }
}
