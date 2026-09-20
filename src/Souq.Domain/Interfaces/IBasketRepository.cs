using Souq.Domain.Entities;

namespace Souq.Domain.Interfaces;

// منفذ كتابة السلال (المرحلة 8). التحميل للعرض والتعديل يشمل الأسطر — قواعد الحدّ والدمج تحتاجها كاملة.
public interface IBasketRepository : IRepository<Basket>
{
    Task<Basket?> GetForCustomerAsync(int customerId, CancellationToken ct = default);

    Task<Basket?> GetForGuestAsync(string guestTokenHash, CancellationToken ct = default);

    // المنتهية الأقدم أولاً، بلا أسطر (الحذف المتتالي في القاعدة يحذفها).
    Task<IReadOnlyList<Basket>> ListExpiredAsync(DateTime now, int max, CancellationToken ct = default);

    // ينسى السلال المتعقَّبة كي تُعاد القراءة على الحالة الملتزمة. المستدعي الوحيد `BasketWriter`.
    void Reset();
}
