using MediatR;
using Souq.Application.Common.Models;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Orders.Queries;

// ============================================================================
// GetOrderTrackingQuery — الاستعلام الوحيد بلا مصادقة في ميزة الطلبات (رابط
// قابل للمشاركة). بما أن معرّفات الطلبات أعداد تسلسلية يسهل تخمينها، عقد هذا
// الاستعلام (OrderTrackingDto) يكشف الحدّ الأدنى فقط لتتبّع الشحنة: الحالة
// وتاريخها ورقم التتبّع — لا هوية العميل ولا عنوانه ولا مبلغ الطلب ولا أسطره.
// أي بيانات إضافية عن الطلب تبقى خلف GetOrderByIdQuery المصادَق والمتحقّق
// من الملكية.
// ============================================================================
public record OrderStatusHistoryDto(string Status, string? Note, DateTime ChangedAt);

public record OrderTrackingDto(
    int OrderId, string Status, string? TrackingNumber, string? ShippingCarrier,
    DateTime CreatedAt, List<OrderStatusHistoryDto> History);

public record GetOrderTrackingQuery(int Id) : IRequest<Result<OrderTrackingDto>>;

public class GetOrderTrackingHandler : IRequestHandler<GetOrderTrackingQuery, Result<OrderTrackingDto>>
{
    private readonly IOrderRepository _orders;
    public GetOrderTrackingHandler(IOrderRepository orders) => _orders = orders;

    public async Task<Result<OrderTrackingDto>> Handle(GetOrderTrackingQuery q, CancellationToken ct)
    {
        var order = await _orders.GetWithItemsAsync(q.Id, ct);
        if (order is null) return Result<OrderTrackingDto>.Failure("الطلب غير موجود", "NotFound");

        // ترتيب زمني تصاعدي (الأقدم أولاً) يطابق اتجاه الخط الزمني في الواجهة
        // (Pending → Paid → Shipped → Delivered). المعرّف يكسر تعادل الطابع
        // الزمني (قد يتشارك انتقالان نفس اللحظة ضمن نفس معاملة الحفظ).
        var history = order.StatusHistory
            .OrderBy(h => h.CreatedAt).ThenBy(h => h.Id)
            .Select(h => new OrderStatusHistoryDto(h.Status.ToString(), h.Note, h.CreatedAt))
            .ToList();

        var dto = new OrderTrackingDto(
            order.Id, order.Status.ToString(), order.TrackingNumber, order.ShippingCarrier,
            order.CreatedAt, history);

        return Result<OrderTrackingDto>.Success(dto);
    }
}
