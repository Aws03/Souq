namespace Souq.Application.Features.Inventory.Contracts;

// سطر حجز: المتغيّر والكمية، واسم للعرض في رسالة نفاد المخزون (Inventory لا تقرأ الكتالوج).
public sealed record ReservationLine(int VariantId, int Quantity, string DisplayName);

// ============================================================================
// عقد الحجز (Modules.md، Architecture.md §6): reserve → commit / release منذ البداية، فيبقى استخراج Inventory ممكناً
// كخطوة saga. المرجع نصّ يملكه المستدعي ("order:{id}")؛ Inventory لا تعرف الطلبات.
// كل عملية تحفظ تغييراتها وتعيد المحاولة عند تعارض التزامن على المخزون نفسه. الحفظ يشمل كل ما هو متتبَّع، لذا
// يحفظ المستدعي تغييراته أولاً، داخل معاملته (IUnitOfWork.InTransactionAsync) إن احتاج الذرّية. لا شبكة داخلها.
// ============================================================================
public interface IInventoryReservations
{
    // يحجز كل الأسطر أو لا شيء: نقص في سطر ⇒ InsufficientStockException (422) ولا حجز.
    Task ReserveAsync(string reference, IReadOnlyList<ReservationLine> lines, CancellationToken ct);

    // الدفع تمّ: الحجوزات النشطة تُلتزم (حركات بيع). مضمون التكرار.
    Task CommitAsync(string reference, CancellationToken ct);

    // إلغاء: النشط يُحرَّر (أو يُعلَّم منتهياً)، والملتزم (طلب مدفوع لم يُشحن) يعود للموجود بحركة إلغاء. مضمون التكرار.
    Task CancelAsync(string reference, string reason, bool expired, CancellationToken ct);

    // مراجع لها حجز نشط انتهت مهلته — لمنسّق انتهاء الطلبات المعلّقة.
    Task<IReadOnlyList<string>> FindExpiredAsync(int max, CancellationToken ct);
}

// المتاح قبل الحجز — لرفض مبكر برسالة واضحة بلا معاملة؛ الحجز نفسه هو الحارس الذرّي.
public interface IStockAvailability
{
    Task<IReadOnlyDictionary<int, int>> AvailableAsync(IReadOnlyCollection<int> variantIds, CancellationToken ct);
}
