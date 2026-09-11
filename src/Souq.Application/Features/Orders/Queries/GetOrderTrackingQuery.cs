using MediatR;
using Souq.Application.Common.Models;

namespace Souq.Application.Features.Orders.Queries;

// ============================================================================
// GetOrderTrackingQuery — الاستعلام الوحيد بلا مصادقة في ميزة الطلبات (رابط قابل للمشاركة).
// بما أن معرّفات الطلبات أعداد تسلسلية يسهل تخمينها، عقد هذا الاستعلام (OrderTrackingDto)
// يكشف الحدّ الأدنى فقط لتتبّع الشحنة: الحالة وتاريخها ورقم التتبّع — لا هوية العميل ولا
// عنوانه ولا مبلغ الطلب ولا أسطره. رموز تتبّع عشوائية بدل المعرّف في المرحلة 9 (B8).
// ============================================================================
public record OrderStatusHistoryDto(string Status, string? Note, DateTime ChangedAt);

public record OrderTrackingDto(
    int OrderId, string Status, string? TrackingNumber, string? ShippingCarrier,
    DateTime CreatedAt, List<OrderStatusHistoryDto> History);

public record GetOrderTrackingQuery(int Id) : IRequest<Result<OrderTrackingDto>>;

public class GetOrderTrackingHandler : IRequestHandler<GetOrderTrackingQuery, Result<OrderTrackingDto>>
{
    private readonly IOrderQueries _orders;
    public GetOrderTrackingHandler(IOrderQueries orders) => _orders = orders;

    public async Task<Result<OrderTrackingDto>> Handle(GetOrderTrackingQuery q, CancellationToken ct)
    {
        var tracking = await _orders.FindTrackingAsync(q.Id, ct);
        return tracking is null
            ? Result<OrderTrackingDto>.Failure(Error.NotFound("الطلب غير موجود"))
            : Result<OrderTrackingDto>.Success(tracking);
    }
}
