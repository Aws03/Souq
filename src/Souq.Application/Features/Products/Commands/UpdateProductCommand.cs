using MediatR;
using Souq.Application.Common.Auditing;
using Souq.Application.Common.Models;
using Souq.Application.Features.Products.Queries;

namespace Souq.Application.Features.Products.Commands;

// ============================================================================
// "أمر" يعدّل منتجاً قائماً (المرحلة 5): النصوص كاملة لكل لغة (تستبدل الحالية)، المعرّف، الفئة، التسعير وSKU
// للمتغيّر الافتراضي، العلامة، الفيديو. Id يفرضه الـ Controller من المسار. الحالة لها أمرها (ChangeProductStatus).
//
// لا مخزون هنا منذ المرحلة 6: تعيين المخزون المطلق من النموذج كان يمحو مبيعات حدثت أثناء فتحه (Phase 0 C4).
// المخزون تصحيحات بفارق وسبب في وحدة Inventory (AdjustStockCommand)، وحدّ التنبيه هناك أيضاً.
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
    string? VideoUrl = null,
    // `null` تمسح التكلفة: التعديل يستبدل الحقول التحريرية كلّها (Product.UpdateVariant).
    decimal? Cost = null) : IRequest<Result>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("catalog.product.updated", "Product", Id.ToString(),
        Metadata: new Dictionary<string, object?> { ["price"] = Price, ["sku"] = Sku });
}
