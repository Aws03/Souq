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
 * @typedef {{price?: number, currency?: string, stockQuantity?: number, brand?: string|null, categoryName?: string|null,
 *            variants?: {price: number, available: number}[]|null} & Record<string, any>} StorefrontProduct
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

  // العرض (V3): منتج بعدّة متغيّرات يمكن شراؤها بأسعار مختلفة يُنشر كنطاق (AggregateOffer) من أسعار ما **يمكن
  // شراؤه الآن** فقط — فلا يُعلَن سعرٌ لمتغيّر معطّل أو نافد، ولا سعرٌ لا يستطيع أحد شراءه به.
  const purchasable = (product.variants ?? []).filter((variant) => variant.available > 0);
  if (purchasable.length > 1 && product.currency) {
    const prices = purchasable.map((variant) => Number(variant.price));
    const low = Math.min(...prices);
    const high = Math.max(...prices);
    data.offers = {
      '@type': 'AggregateOffer',
      lowPrice: String(low),
      highPrice: String(high),
      offerCount: purchasable.length,
      priceCurrency: product.currency,
      availability: `${SCHEMA}/InStock`,
      url,
    };
  } else if (product.price != null && product.currency) {
    // السعر الواحد: سعر المتغيّر الوحيد القابل للشراء، أو سعر المنتج كما حسبه الخادم — والتوفّر من المتاح فعلاً.
    const single = purchasable.length === 1 ? Number(purchasable[0].price) : product.price;
    const inStock = (product.variants ? purchasable.length > 0 : product.stockQuantity > 0);
    data.offers = {
      '@type': 'Offer',
      price: String(single),
      priceCurrency: product.currency,
      availability: `${SCHEMA}/${inStock ? 'InStock' : 'OutOfStock'}`,
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
