using Souq.Domain.Entities;
using Souq.Domain.Enums;

namespace Souq.Domain.Interfaces;

// منفذ الكتابة للطلبات. قوائم العرض وتفاصيله عبر IOrderQueries (ADR-0008).
public interface IOrderRepository : IRepository<Order>
{
    // التجمّع كاملاً (الأسطر + سجلّ الحالة) متتبَّعاً — لانتقالات الحالة والتأكيد.
    Task<Order?> GetWithItemsAsync(int id, CancellationToken ct = default);

    // الحالة الحالية كما هي في قاعدة البيانات الآن (بلا تتبّع) — لحسم سباقات التأكيد
    // المتزامن: النسخة المتتبَّعة في الذاكرة قد تكون قديمة بعد تعارض حفظ.
    Task<OrderStatus?> GetStatusAsync(int id, CancellationToken ct = default);

    // أحدث طلب مُسلَّم للعميل يحوي المنتج (دليل أحقّية التقييم)، أو null. استعلام EXISTS
    // واحد بدل تحميل كل طلبات العميل بأسطرها ثم البحث في الذاكرة.
    Task<int?> FindDeliveredOrderIdContainingAsync(int customerId, int productId, CancellationToken ct = default);
}
