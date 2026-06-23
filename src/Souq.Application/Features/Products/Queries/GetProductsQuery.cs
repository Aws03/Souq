using MediatR;
using Souq.Application.Common.Models;

namespace Souq.Application.Features.Products.Queries;

// ============================================================================
// نمط CQRS: هذا "استعلام" (Query) — يقرأ ولا يُعدّل شيئاً.
// IRequest<...> من MediatR: نُعرّف الطلب ككائن بيانات، ويتولّى MediatR توجيهه
// تلقائياً إلى معالجه (Handler) المناسب. الفائدة: فصل تام بين "ما نريد فعله"
// و"كيف يُفعل"، وكل عملية في ملف معزول قابل للاختبار وحده.
// ============================================================================
public record GetProductsQuery(
    string? Keyword = null,
    int? CategoryId = null,
    int Page = 1,
    int PageSize = 12
) : IRequest<PaginatedList<ProductDto>>;
