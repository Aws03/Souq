using MediatR;
using Souq.Application.Common.Auditing;
using Souq.Application.Common.Models;
using Souq.Application.Features.Products.Queries;
using Souq.Domain.Enums;

namespace Souq.Application.Features.Products.Commands;

// ============================================================================
// "أمر" ينشئ منتجاً (المرحلة 5). النصوص لكل لغة ولغة المتجر الافتراضية شرط؛ السعر وسعر المقارنة وSKU للمتغيّر
// الافتراضي (D-21) بعملة المتجر دائماً. Slug اختياري: يُشتقّ من الاسم اللاتيني إن وُجد وإلا معرّف قصير. الحالة
// الافتراضية نشط (سلوك الإدارة السابق)؛ مسودّة لمنتج يُجهَّز قبل عرضه. الصور تُرفع بعد الإنشاء (نقطة الرفع).
// StockQuantity وLowStockThreshold يفتحان مخزون المتغيّر في وحدة Inventory (المرحلة 6) — التعديل بعدها تصحيحات هناك.
// ============================================================================
public record CreateProductCommand(
    int CategoryId,
    IReadOnlyDictionary<string, CatalogTextInput> Translations,
    decimal Price,
    int StockQuantity,
    decimal? CompareAtPrice = null,
    string? Sku = null,
    string? Slug = null,
    string? Brand = null,
    ProductStatus Status = ProductStatus.Active,
    int LowStockThreshold = Souq.Domain.Entities.InventoryItem.DefaultLowStockThreshold,
    string? VideoUrl = null) : IRequest<Result<int>>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("catalog.product.created", "Product", Slug,
        Metadata: new Dictionary<string, object?> { ["sku"] = Sku, ["status"] = Status.ToString(), ["price"] = Price });
}
