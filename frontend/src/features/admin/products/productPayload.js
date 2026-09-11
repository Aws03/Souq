// ============================================================================
// جسم طلب حفظ منتج من نموذج الإدارة (عقد المرحلة 5) — منطق خالص مُختبَر بـ Vitest.
//
// النصوص لكل لغة (translations)، والسعر وسعر المقارنة وSKU للمتغيّر الافتراضي. المعرّف النصّي (slug) اختياري عند
// الإنشاء (يقترحه الخادم من الاسم) ومطلوب عند التعديل (فارغ ⇒ يبقى الحالي).
//
// المخزون (compare-and-set، ADR-0013): عند التعديل لا نرسل المخزون إلا إن غيّره المدير فعلاً، ومعه
// expectedStockQuantity = القيمة التي رآها حين فتح النموذج. إن بِيع شيء في الأثناء يرفض الخادم الحفظ بـ 409 بدل محو
// البيع (Phase 0 C4)؛ وتعديل الاسم/السعر وحده لا يلمس المخزون أبداً.
// ============================================================================
import { formToTexts } from '../../catalog/catalogText';

const optionalNumber = (value) => (value === '' || value === null || value === undefined ? null : Number(value));

export function buildProductPayload(form, original) {
  const slug = form.slug?.trim().toLowerCase() || null;
  const payload = {
    categoryId: Number(form.categoryId),
    translations: formToTexts(form.texts),
    price: Number(form.price),
    compareAtPrice: optionalNumber(form.compareAtPrice),
    sku: form.sku?.trim() || null,
    brand: form.brand?.trim() || null,
    videoUrl: form.videoRemoved ? null : (original?.videoUrl ?? null),
  };
  const threshold = optionalNumber(form.lowStockThreshold);
  if (threshold !== null) payload.lowStockThreshold = threshold;

  const stock = Number(form.stockQuantity) || 0;
  if (!original) {                                                          // إنشاء: مخزون ابتدائي مطلق
    return { ...payload, slug, status: form.status || 'Active', stockQuantity: stock };
  }

  const edited = { ...payload, slug: slug ?? original.slug };
  if (stock === original.stockQuantity) return edited;                      // المدير لم يلمس المخزون
  return { ...edited, stockQuantity: stock, expectedStockQuantity: original.stockQuantity };
}
