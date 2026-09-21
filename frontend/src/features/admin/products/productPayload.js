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

/**
 * جسم إنشاء/تعديل منتج كما يقبله الخادم. التعديل لا يرسل المخزون ولا الحالة (تصحيحات المخزون
 * وحدة أخرى)، فالشكل المعاد يختلف بين الحالتين — ولذلك النوع اتحاد لا كائن واحد.
 * @param {Record<string, any>} form
 * @param {Record<string, any>} [original]
 * @returns {Record<string, any>}
 */
export function buildProductPayload(form, original) {
  const slug = form.slug?.trim().toLowerCase() || null;
  const payload = {
    categoryId: Number(form.categoryId),
    translations: formToTexts(form.texts),
    price: Number(form.price),
    compareAtPrice: optionalNumber(form.compareAtPrice),
    // التكلفة (C11): فارغ ⇒ null، وnull تمسحها على الخادم. سرٌّ تجاري لا يظهر في أي شاشة متجر.
    cost: optionalNumber(form.cost),
    sku: form.sku?.trim() || null,
    brand: form.brand?.trim() || null,
    videoUrl: form.videoRemoved ? null : (original?.videoUrl ?? null),
  };

  // منتج بخيارات (ADR-0040): السعر وSKU لكل متغيّر من صفحة المتغيّرات. نموذج المنتج يعيد ما قرأه للمتغيّر الافتراضي كما هو،
  // فيقبله الخادم (أي تغيير هنا يرفضه برمز ProductHasVariants) ولا يُعدَّل متغيّر من غير قصد.
  if (original?.options?.length) {
    Object.assign(payload, {
      price: original.price,
      compareAtPrice: original.compareAtPrice ?? null,
      // التكلفة داخل الحارس نفسه: الخادم يرفض تغييرها من نموذج المنتج لمنتجٍ بخيارات، فنُعيد ما قرأناه.
      cost: original.cost ?? null,
      sku: original.sku ?? null,
    });
  }

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
