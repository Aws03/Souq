using MediatR;
using Souq.Application.Common.Auditing;
using Souq.Application.Common.Models;
using Souq.Application.Features.Products.Queries;

namespace Souq.Application.Features.Products.Commands;

// ============================================================================
// "أمر" يعدّل منتجاً قائماً (المرحلة 5): النصوص كاملة لكل لغة (تستبدل الحالية)، المعرّف، الفئة، التسعير وSKU
// للمتغيّر الافتراضي، العلامة، الفيديو. Id يفرضه الـ Controller من المسار. الحالة لها أمرها (ChangeProductStatus).
//
// المخزون (ADR-0013 — compare-and-set): StockQuantity اختياري. null ⇒ لا نمسّ المخزون إطلاقاً (تعديل الاسم/السعر
// لا يكتب فوق مبيعات حدثت أثناء فتح النموذج). إن أُرسل، فـ ExpectedStockQuantity إلزامي = المخزون كما رآه المدير؛
// تغيّر منذ ذلك ⇒ تعارض بدل محو بيع حقيقي (Phase 0 C4).
// ============================================================================
public record UpdateProductCommand(
    int Id,
    int CategoryId,
    IReadOnlyDictionary<string, CatalogTextInput> Translations,
    decimal Price,
    string Slug,
    decimal? CompareAtPrice = null,
    string? Sku = null,
    string? Brand = null,
    int? StockQuantity = null,
    int? ExpectedStockQuantity = null,
    int? LowStockThreshold = null,
    string? VideoUrl = null) : IRequest<Result>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("catalog.product.updated", "Product", Id.ToString(),
        Metadata: new Dictionary<string, object?> { ["price"] = Price, ["stockQuantity"] = StockQuantity });
}
