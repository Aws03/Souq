// ============================================================================
// بيانات منظّمة (schema.org/Product) لصفحة المنتج — منطق خالص مُختبَر.
//
// محرّكات البحث ومنصّات المشاركة تقرأ السعر والتوفّر والتقييم من هذا الكائن لا من الصفحة.
// وكل حقل هنا من الخادم: لا سعر مُخترع، ولا تقييم بلا مقيّمين، ولا توفّر مفترض. الحقل الذي
// لا تعرفه هذه الصفحة يُحذف — بيان منظّم كاذب أسوأ من غيابه (وتُعاقِب عليه المحرّكات).
//
// حدّ معماري موثّق: لا عرض من الخادم في هذا التطبيق، فزاحف لا ينفّذ JavaScript لن يرى هذا
// (FrontendArchitecture) — وهو سبب وجيه لعرض من الخادم لاحقاً، لا سبب لتزييف المحتوى الآن.
// ============================================================================
const SCHEMA = 'https://schema.org';

/**
 * ما تعرفه واجهة المتجر عن منتج (ProductDto من الخادم) — الحقول التي يقرؤها هذا الملف فقط.
 * @typedef {{price?: number, currency?: string, stockQuantity?: number,
 *            brand?: string|null, categoryName?: string|null} & Record<string, any>} StorefrontProduct
 */

/**
 * @param {{product: StorefrontProduct|null, url: string, name: string, description?: string,
 *          image?: string, rating?: {average: number, count: number}|null}} input
 */
export function productStructuredData({ product, url, name, description, image, rating }) {
  if (!product) return null;

  const data = {
    '@context': SCHEMA,
    '@type': 'Product',
    name,
    url,
  };

  if (description) data.description = description;
  if (image) data.image = image;
  if (product.brand) data.brand = { '@type': 'Brand', name: product.brand };
  if (product.categoryName) data.category = product.categoryName;

  if (product.price != null && product.currency) {
    data.offers = {
      '@type': 'Offer',
      price: String(product.price),
      priceCurrency: product.currency,
      availability: `${SCHEMA}/${product.stockQuantity > 0 ? 'InStock' : 'OutOfStock'}`,
      url,
    };
  }

  // تقييم بلا مقيّمين ليس تقييماً: المحرّكات ترفض aggregateRating بعدد صفر، ونحن لا نخترع واحداً.
  if (rating?.count > 0 && rating.average > 0) {
    data.aggregateRating = {
      '@type': 'AggregateRating',
      ratingValue: String(rating.average),
      reviewCount: rating.count,
    };
  }

  return data;
}

// فتات الخبز كبيان منظّم: نفس مسار الصفحة المرئي، لا مسار مخترع للمحرّك.
export function breadcrumbStructuredData(trail) {
  const items = (trail ?? []).filter((step) => step?.name && step?.url);
  if (items.length === 0) return null;
  return {
    '@context': SCHEMA,
    '@type': 'BreadcrumbList',
    itemListElement: items.map((step, index) => ({
      '@type': 'ListItem',
      position: index + 1,
      name: step.name,
      item: step.url,
    })),
  };
}
