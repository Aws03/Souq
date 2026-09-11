using MediatR;
using Souq.Application.Common.Models;
using Souq.Domain.Entities;

namespace Souq.Application.Features.Orders.Queries;

// ============================================================================
// GetOrderTrackingQuery — الاستعلام الوحيد بلا مصادقة في ميزة الطلبات (رابط قابل للمشاركة). منذ المرحلة 9 بالرمز
// العشوائي (128 بت) لا بالمعرّف التسلسلي، فلا تعداد (B8). العقد يكشف الحدّ الأدنى لتتبّع الشحنة: الرقم والحالة
// وتواريخها ورقم التتبّع — لا هوية العميل ولا عنوانه ولا مبلغه ولا أسطره، ولا ملاحظات الإدارة ولا من غيّر الحالة.
// رمز بشكل خاطئ = غير موجود (404)، لا خطأ تحقّق يميّز الشكل من المحتوى.
// ============================================================================
public record OrderTrackingStepDto(string Status, DateTime ChangedAt);

public record OrderTrackingDto(
    int OrderNumber, string Status, string? TrackingNumber, string? ShippingCarrier,
    DateTime CreatedAt, List<OrderTrackingStepDto> History);

public record GetOrderTrackingQuery(string Token) : IRequest<Result<OrderTrackingDto>>;

public class GetOrderTrackingHandler : IRequestHandler<GetOrderTrackingQuery, Result<OrderTrackingDto>>
{
    private readonly IOrderQueries _orders;
    public GetOrderTrackingHandler(IOrderQueries orders) => _orders = orders;

    public async Task<Result<OrderTrackingDto>> Handle(GetOrderTrackingQuery q, CancellationToken ct)
    {
        var token = q.Token?.Trim().ToLowerInvariant() ?? "";
        var tracking = token.Length == Order.TrackingTokenLength && token.All(char.IsAsciiHexDigitLower)
            ? await _orders.FindTrackingAsync(token, ct)
            : null;
        return tracking is null
            ? Result<OrderTrackingDto>.Failure(Error.NotFound("الطلب غير موجود"))
            : Result<OrderTrackingDto>.Success(tracking);
    }
}
