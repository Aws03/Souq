using Souq.Application.Common.Models;

namespace Souq.Application.Features.Reviews.Queries;

// ============================================================================
// منفذ القراءة لوحدة Reviews (ADR-0008). اسم المقيِّم يُجلب بالربط (JOIN) في الاستعلام نفسه
// — كان المعالج يجلب كل عميل على حدة لكل تقييم (N+1، Phase 0 C13): 10 تقييمات = 12 استعلاماً.
// الآن ثلاثة ثابتة أيّاً كان حجم الصفحة: العدد، المتوسط، الصفحة.
// ============================================================================
public interface IReviewQueries
{
    Task<ProductReviewsDto> ListForProductAsync(int productId, PageRequest page, CancellationToken ct);
}
