// ============================================================================
// جسم طلب حفظ منتج من نموذج الإدارة — منطق خالص مُختبَر بـ Vitest.
//
// المخزون (compare-and-set، ADR-0013): عند التعديل لا نرسل المخزون إلا إن غيّره
// المدير فعلاً، ومعه expectedStockQuantity = القيمة التي رآها حين فتح النموذج. إن
// بِيع شيء في الأثناء يرفض الخادم الحفظ بـ 409 بدل محو البيع (Phase 0 C4)؛ وتعديل
// الاسم/السعر وحده لا يلمس المخزون أبداً.
// ============================================================================
export function buildProductPayload(form, original) {
  const payload = {
    nameAr: form.nameAr.trim(),
    nameEn: form.nameEn.trim() || null,
    description: form.description.trim(),
    price: Number(form.price),
    categoryId: Number(form.categoryId),
    imageUrl: original?.imageUrl ?? '',
    videoUrl: form.videoRemoved ? null : (original?.videoUrl ?? null),
  };

  const stock = Number(form.stockQuantity) || 0;
  if (!original) return { ...payload, stockQuantity: stock };                // إنشاء: مخزون ابتدائي مطلق
  if (stock === original.stockQuantity) return payload;                      // المدير لم يلمس المخزون
  return { ...payload, stockQuantity: stock, expectedStockQuantity: original.stockQuantity };
}
