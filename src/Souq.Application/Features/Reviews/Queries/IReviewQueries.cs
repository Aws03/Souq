using Souq.Application.Common.Models;
using Souq.Application.Features.Reviews.Moderation;

namespace Souq.Application.Features.Reviews.Queries;

// ============================================================================
// منفذ القراءة لوحدة Reviews (ADR-0008). اسم المقيِّم يُجلب بالربط (JOIN) في الاستعلام نفسه
// — كان المعالج يجلب كل عميل على حدة لكل تقييم (N+1، Phase 0 C13): 10 تقييمات = 12 استعلاماً.
// الآن ثلاثة ثابتة أيّاً كان حجم الصفحة: التوزيع (ومنه العدد والمتوسط)، عدد الصفحات، الصفحة.
// ============================================================================
public interface IReviewQueries
{
    // العرض العام: المعتمد وحده (المرحلة 13) — المعلّق والمرفوض لا يظهران ولا يدخلان المتوسط.
    Task<ProductReviewsDto> ListForProductAsync(int productId, PageRequest page, CancellationToken ct);

    // قائمة المشرف: كل الحالات، الأحدث أولاً. culture: لغة المتجر الافتراضية لاسم المنتج.
    Task<PaginatedList<AdminReviewDto>> ListForModerationAsync(
        ReviewModerationFilter filter, PageRequest page, string culture, CancellationToken ct);
}
