namespace Souq.Application.Features.Products.Contracts;

// ============================================================================
// منفذ تملكه Catalog وتنفّذه Inventory (عكس الاعتماد — Modules.md §2: Inventory → Catalog): حين يُنشأ متغيّر قابل
// للبيع يُفتح له مخزون بكمية ابتدائية وحدّ تنبيه. Catalog لا تعرف وحدة المخزون ولا جداولها.
// يُستدعى بعد حفظ المنتج (المعرّفات موجودة)، داخل معاملة المستدعي كي يُنشأ المنتج ومخزونه معاً أو لا شيء.
// ============================================================================
public interface IVariantStockInitializer
{
    Task InitializeAsync(int productId, int variantId, int initialQuantity, int lowStockThreshold, CancellationToken ct);
}
