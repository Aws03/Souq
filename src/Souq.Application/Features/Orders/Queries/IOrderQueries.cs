using Souq.Application.Common.Models;

namespace Souq.Application.Features.Orders.Queries;

// ============================================================================
// IOrderQueries — منفذ القراءة لوحدة Ordering (ADR-0008). الملخّصات إسقاط SQL بمجاميع
// محسوبة في القاعدة (لا تحميل أسطر كل طلب)؛ تفاصيل طلب واحد تُحمَّل بلا تتبّع وتُحسب
// إجمالياتها بقواعد المجال نفسها. الملكية لا تُقرَّر هنا: المعالج يطابق CustomerId مع
// ICurrentUser (404 لمن لا يملك).
// ============================================================================
public interface IOrderQueries
{
    // كل الطلبات (الإدارة) — الأحدث أولاً.
    Task<PaginatedList<OrderSummaryDto>> ListAsync(PageRequest page, CancellationToken ct);

    // طلبات عميل واحد — الأحدث أولاً.
    Task<PaginatedList<OrderSummaryDto>> ListForCustomerAsync(int customerId, PageRequest page, CancellationToken ct);

    Task<OrderDto?> FindAsync(int id, CancellationToken ct);

    Task<OrderTrackingDto?> FindTrackingAsync(int id, CancellationToken ct);
}
