namespace Souq.Application.Features.Analytics.Contracts;

// ============================================================================
// صيانةُ مخزن الأحداث: التجميع، ثم المسح — **بهذا الترتيب، وهو ترتيبٌ يفرضه الكود** ([ADR-0050](0050) §6).
//
// منفذان لا واحد، كما فُصِل `ISearchLog` عن `ISearchLogRetention`: الكتابةُ والقراءةُ والمسحُ
// أعمالٌ لها ضمانات مختلفة، وجمعُها في منفذٍ واحد يجعل مُنادياً يملك ما لا يحتاجه.
// ============================================================================

// تجميعُ يومٍ كاملاً: يُحتسب من الصفوف الخام ويُستبدَل به ما كان لذلك اليوم — فإعادةُ التشغيل
// تُنتج الرقم نفسه ولا تجمعه فوق نفسه.
public interface IEventRollups
{
    // اليومُ التالي الذي ينتظر التجميع (أقدمُ يومٍ فيه أحداث بعد علامة التجميع)، أو null إن لم
    // يبقَ يومٌ **مكتمل** — فاليوم الجاري لا يُجمَّع: أحداثه لم تنتهِ بعد.
    Task<DateTime?> NextDayToRollUpAsync(DateTime utcNow, CancellationToken ct);

    // يعيد عدد صفوف التجميع المكتوبة (تفاعلُ المنتجات + أزواجها).
    Task<int> RollUpAsync(DateTime day, DateTime utcNow, CancellationToken ct);

    // آخرُ يومٍ مكتمل جُمِّع، أو null. هو **الحدّ الذي لا يتجاوزه المسح**.
    Task<DateTime?> RolledUpThroughAsync(CancellationToken ct);
}

// مسحُ الصفوف الخام وحدها. التجميعات لا تُمسح: لا معرّف شخص فيها، فمدّة حفظها هندسية.
public interface IEventStoreRetention
{
    Task<int> PurgeBeforeAsync(DateTime before, int max, CancellationToken ct);
}
