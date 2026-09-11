using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Orders;

// ============================================================================
// OrderStockRelease — يُعيد مخزون طلب أُلغي قبل شحنه ويسجّل كل إعادة في سجلّ الحركة.
// مكان واحد لهذه القاعدة يستخدمه مساران: إلغاء الإدارة، وتعويض فشل الدفع/بدء الدفع.
// قبل Phase 1A كان الأول لا يعيد المخزون إطلاقاً (خسارة دائمة لكل طلب ملغى) والثاني
// يعيده بلا أثر في السجلّ (سجلّ لا يطابق المخزون) — Phase 0 C2/C3.
//
// يُستدعى مباشرة بعد order.Cancel() الناجح: Cancel نفسه يضمن أن الطلب كان يحجز
// مخزوناً (Pending/Paid) وأنه لم يُلغَ من قبل، فالإعادة تحدث مرة واحدة فقط. لا تُحفَظ
// التغييرات هنا — المستدعي يحفظ الإلغاء والإعادة معاً في معاملة واحدة.
// ============================================================================
public sealed class OrderStockRelease
{
    private readonly IProductRepository _products;
    private readonly IStockMovementRepository _movements;

    public OrderStockRelease(IProductRepository products, IStockMovementRepository movements)
    {
        _products = products; _movements = movements;
    }

    public async Task ReleaseAsync(Order order, string reason, CancellationToken ct)
    {
        foreach (var item in order.Items)
        {
            var product = await _products.GetByIdAsync(item.ProductId, ct);
            if (product is null) continue; // منتج لم يعد موجوداً — لا مخزون نعيده إليه

            product.IncreaseStock(item.Quantity);
            _products.Update(product);
            await _movements.AddAsync(
                StockMovement.For(product, StockMovementType.Cancellation, item.Quantity, reason), ct);
        }
    }
}
