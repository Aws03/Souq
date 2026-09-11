using Souq.Application.Common.Models;
using Souq.Domain.Enums;

namespace Souq.Application.Features.Orders.Queries;

// ============================================================================
// IOrderQueries — منفذ القراءة لوحدة Ordering (ADR-0008). الملخّصات إسقاط SQL من الإجماليات المثبَّتة على الطلب (المرحلة
// 9) بلا تحميل أسطر؛ تفاصيل طلب واحد تُحمَّل بلا تتبّع وتُحسب إجمالياتها بقواعد المجال نفسها. الملكية وما يراه كل ناظر
// لا تُقرَّر هنا: المعالج يطابق CustomerId مع ICurrentUser (404 لمن لا يملك) ويشكّل العقد للعميل أو للإدارة.
// ============================================================================
public interface IOrderQueries
{
    // طلبات المتجر بالمرشّحات — الأحدث أولاً.
    Task<PaginatedList<OrderSummaryDto>> ListAsync(OrderFilter filter, PageRequest page, CancellationToken ct);

    // طلبات عميل واحد — الأحدث أولاً.
    Task<PaginatedList<OrderSummaryDto>> ListForCustomerAsync(int customerId, PageRequest page, CancellationToken ct);

    // التفاصيل كاملةً (بالملاحظات ومن غيّر الحالة) — المعالج يحذف ما لا يخصّ الناظر.
    Task<OrderDto?> FindAsync(int id, CancellationToken ct);

    Task<OrderTrackingDto?> FindTrackingAsync(string token, CancellationToken ct);
}

// مرشّحات قائمة الطلبات (المرحلة 9). Search: رقم طلب بالضبط ("1042" أو "#1042")، أو جزء من اسم العميل أو بريده.
// From/To على لحظة الإنشاء (To حصري).
public sealed record OrderFilter(
    int? CustomerId = null, OrderStatus? Status = null, string? Search = null, DateTime? From = null, DateTime? To = null);
