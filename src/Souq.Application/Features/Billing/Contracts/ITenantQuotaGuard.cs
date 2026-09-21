using Souq.Application.Common.Models;

namespace Souq.Application.Features.Billing.Contracts;

// ============================================================================
// حارس الحصص (C2، ADR-0049) — **المنفذ الوحيد** الذي يُرفض به إنشاءُ الشيء رقم (ن+1).
//
// عقدُه مع المستدعي ثلاث جمل:
//   1. `ReserveAsync` **كتابة**، لا سؤال. تزيد العدّاد وتردّ ما إذا كان الحجز وقع — وعليه فهي
//      تُستدعى داخل معاملة المستدعي حصراً، فإن فشل ما بعدها تراجَع الحجز معها. استدعاؤها خارج
//      معاملة خطأ برمجي يُرمى عنده لا يُتسامح معه.
//   2. الترتيب: **احجز ثم أنشئ**. العكس (أنشئ ثم احجز) يعمل أيضاً، لكنه يجعل الرفض تراجُعاً عن
//      كتابةٍ تمّت بدل أن يكون امتناعاً عنها — وهو ما يجعل الفشل أغلى ويُطيل القفل بلا داعٍ.
//   3. `ReleaseAsync` التزام لا خيار: العدّاد صورةٌ عن الحقيقة، ومن لا يُنقِصه عند الحذف يترك
//      انحرافاً يسدّ متجراً بعد حين. المسح المصالِح شبكة أمان لا بديل.
//
// ولماذا منفذ واحد لا فحصٌ في كل مسار؟ لأن العدّ-ثم-الكتابة يفشل مفتوحاً بصمت، فالمواضع المتعدّدة
// تعني احتمالات فشلٍ متعدّدة لا تُكتشف. اختبار معماري يمنع الموضع الثاني (TenancyRuleTests).
// ============================================================================

// نتيجة محاولة حجز. `Limit` فارغةٌ تعني **غير مقيَّد** — لا صفراً ولا لا نهاية بلا معنى:
// الخطة لم تسمِّ هذا الحدّ، فلا قيد. الحجّة كاملةً (ولماذا هي غير متناظرة مع الاستحقاق الذي
// يفشل مغلقاً) في ADR-0054.
public sealed record QuotaDecision(string LimitName, bool Allowed, int? Limit, int Used)
{
    public static QuotaDecision Uncapped(string limitName, int used) => new(limitName, true, null, used);
    public static QuotaDecision Granted(string limitName, int limit, int used) => new(limitName, true, limit, used);
    public static QuotaDecision Denied(string limitName, int limit, int used) => new(limitName, false, limit, used);

    // رمز واحد لكل الحدود، والاسم في التفاصيل: العميل يتفرّع على الرمز لا على الرسالة
    // (ApiDocumentation.md)، وسنُّ رمزٍ لكل حدّ كان سيُلزم كل عميل بتحديث عند كل حدّ جديد.
    public Error ToError() => Error.Conflict("QuotaExceeded",
        $"بلغ هذا المتجر حدّ خطته ({Limit}) — رقّ الخطة أو احذف ما لم يعد مستعملاً");
}

public interface ITenantQuotaGuard
{
    // يحجز وحدةً واحدة من حدّ المتجر الحالي. تُستدعى داخل معاملة المستدعي حصراً.
    Task<QuotaDecision> ReserveAsync(string limitName, CancellationToken ct = default);

    // يردّ ما استُهلك بعد حذفٍ فعليّ. لا تُنقِص تحت الصفر، ولا تحتاج معاملة: التأخّر في الإنقاص
    // يُضيّق مؤقّتاً ولا يفتح شيئاً، والمسح المصالِح يُصلحه.
    Task ReleaseAsync(string limitName, int count = 1, CancellationToken ct = default);

    // للعرض وحده: يقرأ بلا قفل وبلا كتابة (ADR-0049 §التبعات — صفحةٌ تعرض المتبقّي يجب ألّا تحجز).
    Task<QuotaDecision> PeekAsync(string limitName, CancellationToken ct = default);

    // يُصالح عدّادات متجر السياق مع الحقيقة ويعيد عدد ما صُحِّح. **جزء من الآلية لا متابعةٌ لها**
    // (ADR-0049 §الالتزامات): عدّادٌ يُنقَص بيدٍ هو عدّادٌ ينحرف، ومسارٌ نسي ReleaseAsync لا يُكتشف
    // بغير هذا. يُنادى من منسّق دوري لكل متجر.
    Task<int> ReconcileAsync(CancellationToken ct = default);
}
