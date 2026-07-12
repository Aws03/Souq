namespace Souq.Application.Features.Products.Queries;

// ============================================================================
// ProductDto — لماذا لا نُرجع كيان Product مباشرة للـ API؟
// 1) أمان: قد يحوي الكيان حقولاً داخلية لا يجب أن يراها العميل.
// 2) استقرار: لو غيّرنا بنية الكيان الداخلي، لا يتعطّل الـ Frontend ما دام
//    شكل الـ DTO ثابتاً. الـ DTO هو "العقد" المستقر مع العالم الخارجي.
// 3) تسطيح: نحوّل Money (كائن) إلى رقم + عملة بسيطين يسهل على JSON تمثيلهما.
// ============================================================================
public record ProductDto(
    int Id, string NameAr, string NameEn, string Description,
    decimal Price, string Currency,
    int StockQuantity, string ImageUrl,
    int CategoryId, string? CategoryName);
