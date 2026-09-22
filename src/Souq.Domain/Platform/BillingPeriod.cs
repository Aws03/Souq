using Souq.Domain.Common;
using Souq.Domain.Exceptions;

namespace Souq.Domain.Platform;

// ============================================================================
// حالةُ فترة الفوترة ([ADR-0056](0056)، CommercialPlatformArchitecture §4.5).
//
// **ولماذا ثلاثُ حالاتٍ لا اثنتان؟** لأنّ الإغلاق ليس لحظةً: بين «توقّف عن قبول أحداثٍ جديدة»
// و«صارت الأرقامُ نهائية» عملٌ يجري — تُجمَع الأحداث، وتُصنَع الفاتورة، وقد يفشل ذلك في منتصفه.
// فحالةٌ وسطى تجعل الفشلَ قابلاً للاستئناف بلا أن تعود الفترةُ تقبل أحداثاً بأثرٍ رجعيّ.
//
//   • Open    — تقبل الأحداث.
//   • Closing — لا تقبل جديداً، وأرقامُها لم تُعتمَد بعد.
//   • Closed  — نهائية. **لا يلتحق بها شيءٌ أبداً**، والحدثُ المتأخّر يذهب إلى الفترة التالية.
//
// والقاعدةُ الأخيرة هي جوهرُ الأمر: حدثٌ يلتحق بفترةٍ صدرت فاتورتُها يعني فاتورةً لا تساوي ما
// تحته — وهي أسوأ من حدثٍ يتأخّر شهراً.
// ============================================================================
public enum BillingPeriodStatus
{
    Open = 0,
    Closing = 1,
    Closed = 2,
}

// ============================================================================
// فترةُ فوترةٍ لمتجرٍ واحد: جدولُ منصّةٍ **بمفتاح متجر** (الشكل B) — فلا مرشّحَ عليه، وكلُّ قراءةٍ
// تخصّ متجراً تكتب شرطَ `TenantId` بيدها.
// ============================================================================
public class BillingPeriod : Entity
{
    public int TenantId { get; private set; }
    public DateTime StartsAtUtc { get; private set; }

    // نهايةُ الفترة **حصريّة**: فترةُ شهرٍ تبدأ في الأول وتنتهي في أوّل الشهر التالي، فلا تتداخل
    // فترتان ولا تسقط لحظةٌ بينهما. والحدثُ يلتحق بفترةٍ إن كان `Start ≤ t < End`.
    public DateTime EndsAtUtc { get; private set; }

    public BillingPeriodStatus Status { get; private set; }
    public DateTime? ClosedAtUtc { get; private set; }

    private BillingPeriod() { }

    public BillingPeriod(int tenantId, DateTime startsAtUtc, DateTime endsAtUtc)
    {
        if (tenantId <= 0) throw new InvalidBillingPeriodException("فترةُ فوترةٍ بلا متجر");
        if (endsAtUtc <= startsAtUtc)
            throw new InvalidBillingPeriodException("نهايةُ الفترة بعد بدايتها");
        TenantId = tenantId;
        StartsAtUtc = startsAtUtc;
        EndsAtUtc = endsAtUtc;
        Status = BillingPeriodStatus.Open;
    }

    public bool AcceptsEvents => Status == BillingPeriodStatus.Open;

    public bool Contains(DateTime instant) => instant >= StartsAtUtc && instant < EndsAtUtc;

    // بدءُ الإغلاق: تتوقّف عن قبول الأحداث. تُستدعى مرّتين بلا ضرر — استئنافُ إغلاقٍ تعثّر يمرّ
    // من هنا، فجعلُها ترمي على حالتها الحالية كان سيمنع الاستئناف بالضبط.
    public void BeginClose()
    {
        if (Status == BillingPeriodStatus.Closed)
            throw new InvalidBillingPeriodException("الفترةُ مغلقة — لا تُفتح ولا يُعاد إغلاقها");
        Status = BillingPeriodStatus.Closing;
    }

    // الإغلاقُ النهائيّ. لا يقع إلا بعد `BeginClose`: القفزُ من «مفتوحة» إلى «مغلقة» يعني أنّ
    // أحداثاً كانت تُقبَل في اللحظة التي كانت أرقامُها تُجمَع فيها.
    public void Close(DateTime utcNow)
    {
        if (Status != BillingPeriodStatus.Closing)
            throw new InvalidBillingPeriodException("تُغلَق الفترةُ التي بدأ إغلاقُها وحدها");
        Status = BillingPeriodStatus.Closed;
        ClosedAtUtc = utcNow;
    }
}

// ============================================================================
// حدثٌ قابلٌ للفوترة: سجلٌّ **مُلحَقٌ لا يُعدَّل** بوحدةٍ قابلة للفوترة وقعت لمتجر (الشكل B).
//
// **وليس قياساً عن بُعد.** القياسُ يُعيَّن ويُسقَط عند الازدحام، وما يُسقَط لا يُفوتَر. فهذا هو
// مجرى الأحداث الوحيد في التصميم الذي **لا يمرّ** بقناة «أسقِط عند الامتلاء» التي تستعملها
// الأحداثُ السلوكية (ADR-0050): يُكتب في معاملة مُنادِيه أو لا يُكتب، ويُقال له.
//
// **والمفتاحُ الذي يمنع التكرار تسكّه سوق نفسها** لا مصدرٌ خارجيّ: إعادةُ محاولةٍ بعد انقطاعٍ
// شبكيّ تُرسل الحدث مرّتين، وبلا مفتاحٍ ثابتٍ يُحسَب من محتواه تصير الوحدةُ مفوترةً مرّتين —
// وذلك مالٌ يُطالَب به بلا سبب. الفهرسُ الفريد على (المتجر، المفتاح) هو ما يحسمه في القاعدة لا
// في الذاكرة.
//
// **ولا كتالوجَ مغلقٌ للمقاييس اليوم، وهذا مكتوبٌ كي لا يُفترض خلافُه.** `LimitNames` كتالوجٌ
// مغلق لأنّ الآلة يجب أن تعرف **كيف تعدّ** كلَّ اسمٍ فيه؛ والمقياسُ هنا يكتبه مَن يُصدره، ولا
// شيءَ في المنتج يُصدر مقياساً بعد. فالشكلُ محروس (حروفٌ صغيرة ونقاط) والمعنى ليس — ويومَ يوجد
// أوّلُ مُصدِرٍ حقيقيّ يصير كتالوجاً مغلقاً كنظيره، لا قبله.
// ============================================================================
public class BillableEvent : Entity
{
    public const int MeterMaxLength = 60;
    public const int IdempotencyKeyMaxLength = 100;
    public const int DescriptionMaxLength = 300;

    public int TenantId { get; private set; }
    public string Meter { get; private set; } = default!;
    public decimal Quantity { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
    public string IdempotencyKey { get; private set; } = default!;
    public string? Description { get; private set; }

    // الفترةُ التي التحق بها الحدث. تُسنَد عند الكتابة — الحدثُ يجد فترتَه المفتوحة أو تُفتَح له.
    public int BillingPeriodId { get; private set; }

    // الفاتورةُ التي حُمِّل عليها، إن حُمِّل. `null` ⇒ لم يُفوتَر بعد — وهو ما تقرؤه صناعةُ
    // الفاتورة كي لا تُحمّل وحدةً مرّتين.
    public int? PlatformInvoiceId { get; private set; }

    private BillableEvent() { }

    public BillableEvent(
        int tenantId, string meter, decimal quantity, DateTime occurredAtUtc,
        string idempotencyKey, BillingPeriod period, string? description = null)
    {
        ArgumentNullException.ThrowIfNull(period);
        if (tenantId <= 0) throw new InvalidBillingPeriodException("حدثٌ قابلٌ للفوترة بلا متجر");
        if (period.TenantId != tenantId)
            throw new InvalidBillingPeriodException("الحدثُ وفترتُه لمتجرَين مختلفَين");
        if (!period.AcceptsEvents)
            throw new InvalidBillingPeriodException("لا يلتحق حدثٌ بفترةٍ أُغلقت — يذهب إلى الفترة التالية");
        if (!period.Contains(occurredAtUtc))
            throw new InvalidBillingPeriodException("لحظةُ الحدث خارج مدّة فترته");
        if (quantity <= 0)
            throw new InvalidBillingPeriodException("كمّيةُ الحدث القابل للفوترة أكبر من صفر");

        TenantId = tenantId;
        Meter = NormalizeMeter(meter);
        Quantity = quantity;
        OccurredAtUtc = occurredAtUtc;
        IdempotencyKey = NormalizeKey(idempotencyKey);
        BillingPeriodId = period.Id;
        Description = NormalizeDescription(description);
    }

    // التحميلُ على فاتورة يقع مرّةً واحدة. محاولةُ تحميلِ حدثٍ محمَّلٍ ترمي بدل أن تتجاهل: وحدةٌ
    // تظهر في فاتورتين مالٌ يُطالَب به مرّتين، والصمتُ عنه يجعله يُكتشف من شكوى تاجر.
    public void BillOn(int platformInvoiceId)
    {
        if (PlatformInvoiceId is not null)
            throw new InvalidBillingPeriodException($"الحدثُ محمَّلٌ على الفاتورة {PlatformInvoiceId} — لا يُحمَّل مرّتين");
        if (platformInvoiceId <= 0) throw new InvalidBillingPeriodException("معرّفُ الفاتورة غير صالح");
        PlatformInvoiceId = platformInvoiceId;
    }

    private static string NormalizeMeter(string? meter)
    {
        var trimmed = meter?.Trim().ToLowerInvariant() ?? "";
        if (trimmed.Length is 0 or > MeterMaxLength)
            throw new InvalidBillingPeriodException($"اسمُ المقياس مطلوب، حتى {MeterMaxLength} حرفاً");
        // الشكلُ نفسه الذي تتبعه أسماءُ الحدود والاستحقاقات: `a.b.c` — فتُقرأ الأسماءُ الثلاثة
        // معاً في شاشةٍ واحدة بلا أن يبدو أحدُها غريباً.
        if (!trimmed.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '.' or '-'))
            throw new InvalidBillingPeriodException("اسمُ المقياس حروفٌ لاتينية صغيرة وأرقامٌ ونقاطٌ وشرطات");
        return trimmed;
    }

    private static string NormalizeKey(string? key)
    {
        var trimmed = key?.Trim() ?? "";
        if (trimmed.Length is 0 or > IdempotencyKeyMaxLength)
            throw new InvalidBillingPeriodException($"مفتاحُ عدم التكرار مطلوب، حتى {IdempotencyKeyMaxLength} حرفاً");
        if (trimmed.Any(char.IsControl))
            throw new InvalidBillingPeriodException("مفتاحُ عدم التكرار يحتوي محارف غير مسموحة");
        return trimmed;
    }

    private static string? NormalizeDescription(string? description)
    {
        var trimmed = description?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        if (trimmed.Length > DescriptionMaxLength)
            throw new InvalidBillingPeriodException($"وصفُ الحدث حتى {DescriptionMaxLength} حرفاً");
        return trimmed;
    }
}
