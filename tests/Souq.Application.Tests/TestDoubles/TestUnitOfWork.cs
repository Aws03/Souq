using NSubstitute;
using Souq.Domain.Interfaces;

namespace Souq.Application.Tests.TestDoubles;

// وحدة عمل وهمية تنفّذ جسم المعاملة فوراً (بلا قاعدة): حالات الاستخدام التي تلفّ حفظاتها بمعاملة تُختبر كما تعمل،
// والاستثناء من داخلها يصعد كما في الحقيقة (والتراجع مسؤولية القاعدة — تُثبته اختبارات التكامل).
public static class TestUnitOfWork
{
    // ============================================================================
    // ضعفٌ **جزئي** على صنف حقيقي، لا بديل كامل (C2) — والسبب أن NSubstitute يضبط كل صيغة مغلقة
    // من الطريقة المُعمَّمة على حدة: `InTransactionAsync<T>` لا تُضبَط إلّا بمعرفة T مسبقاً، فأيّ
    // حالة استخدام بـ T جديد كانت ستتلقّى `null` لـ `Task<T>` وتسقط بـ NullReferenceException.
    //
    // وهذا ليس افتراضاً: هو ما وقع فعلاً حين صار إنشاءُ المنتج يُرجع `QuotaDecision` من معاملته.
    // الصنف أدناه يُنفّذ الصيغتين بنفسه لأيّ T، والضعف الجزئي يُبقي `Received()`/`DidNotReceive()`
    // على `SaveChangesAsync` تعمل كما كانت.
    // ============================================================================
    public static IUnitOfWork Create() => Substitute.ForPartsOf<RecordingUnitOfWork>();
}

public class RecordingUnitOfWork : IUnitOfWork
{
    public virtual Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);

    public virtual Task<T> InTransactionAsync<T>(Func<Task<T>> work, CancellationToken ct = default) => work();

    public virtual Task InTransactionAsync(Func<Task> work, CancellationToken ct = default) => work();

    // العزل لا معنى له بلا قاعدة: يُسجَّل آخر ما طُلب كي تستطيع الاختبارات تأكيده، ويُنفَّذ
    // الجسم كما هو. أثرُه الحقيقي يُقاس في اختبارات التكامل على قاعدة تعمل بلقطات فعلاً.
    public TransactionIsolation LastIsolation { get; private set; } = TransactionIsolation.Default;

    public virtual Task InTransactionAsync(
        Func<Task> work, TransactionIsolation isolation, CancellationToken ct = default)
    {
        LastIsolation = isolation;
        return work();
    }
}
