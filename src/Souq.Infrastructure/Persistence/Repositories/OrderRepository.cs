using Microsoft.EntityFrameworkCore;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Interfaces;

namespace Souq.Infrastructure.Persistence.Repositories;

public class OrderRepository : RepositoryBase<Order>, IOrderRepository
{
    public OrderRepository(AppDbContext db) : base(db) { }

    // نجلب الطلب مع أسطره وسجلّ تاريخه (التجمّع كاملاً) عبر Include. تضمين
    // StatusHistory هنا ليس للقراءة فقط: هذا المُستدعى الوحيد الذي تُبنى عليه
    // انتقالات الحالة (UpdateOrderStatusHandler/OrderPaymentConfirmation)،
    // وبلا تحميله مسبقاً لن يكتشف تتبّع التغييرات في EF سطر التاريخ الجديد
    // المُضاف داخلياً عبر RecordStatusChange عند الحفظ.
    public async Task<Order?> GetWithItemsAsync(int id, CancellationToken ct = default)
        => await Db.Orders.Include(o => o.Items).Include(o => o.StatusHistory)
                          .FirstOrDefaultAsync(o => o.Id == id, ct);

    // إسقاط قيمة واحدة (Select) لا يُتتبَّع أصلاً — نقرأ الحالة الحقيقية من القاعدة الآن
    // حتى لو كانت نسخة متتبَّعة قديمة من نفس الطلب في الذاكرة بعد تعارض حفظ.
    public async Task<OrderStatus?> GetStatusAsync(int id, CancellationToken ct = default)
        => await Db.Orders.Where(o => o.Id == id).Select(o => (OrderStatus?)o.Status).FirstOrDefaultAsync(ct);

    // الأحدث أولاً (نفس اختيار السلوك السابق حين كانت كل الطلبات تُحمَّل ثم يُبحث فيها).
    public async Task<int?> FindDeliveredOrderIdContainingAsync(int customerId, int productId, CancellationToken ct = default)
        => await Db.Orders
            .Where(o => o.CustomerId == customerId && o.Status == OrderStatus.Delivered
                        && o.Items.Any(i => i.ProductId == productId))
            .OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id)
            .Select(o => (int?)o.Id)
            .FirstOrDefaultAsync(ct);
}
