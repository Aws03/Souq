// ============================================================================
// جسم طلب حفظ منتج من نموذج الإدارة — منطق خالص مُختبَر بـ Vitest.
//
// النصوص لكل لغة (translations)، والسعر وسعر المقارنة وSKU للمتغيّر الافتراضي (المرحلة 5). المعرّف النصّي (slug)
// اختياري عند الإنشاء (يقترحه الخادم) ومطلوب عند التعديل (فارغ ⇒ يبقى الحالي).
//
// المخزون (المرحلة 6، ADR-0026): الكمية الابتدائية وحدّ التنبيه يُرسلان عند الإنشاء فقط (يفتحان مخزون المنتج). بعدها
// المخزون تصحيحات بفارق وسبب من صفحة الجرد — نموذج المنتج لا يرسل مخزوناً أبداً، فلا يمحو بيعاً حدث أثناء فتحه
// (Phase 0 C4).
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

  if (original) return { ...payload, slug: slug ?? original.slug };

  const threshold = optionalNumber(form.lowStockThreshold);
  return {
    ...payload,
    slug,
    status: form.status || 'Active',
    stockQuantity: Number(form.stockQuantity) || 0,
    ...(threshold !== null ? { lowStockThreshold: threshold } : {}),
  };
}
