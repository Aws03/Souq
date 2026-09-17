// ============================================================================
// السلة من الخادم (المرحلة 8) — منطق خالص مُختبَر: أسطر /api/basket بشكل عناصر السلة الذي تعرضه المكوّنات (id = معرّف
// المنتج، qty، price، currency، translations، imageUrl)، ومشاكل الأسطر التي تمنع الدفع، ورسالة كوبون مرفوض.
// ============================================================================
export const MAX_QUANTITY = 99; // Basket.MaxQuantityPerLine في Domain

// سلة فارغة قبل ردّ الخادم: بلا عملة — formatPrice يأخذ عملة المتجر من إعداده (المرحلة 15).
export const EMPTY_BASKET = {
  lines: [], itemCount: 0, currency: '', subtotal: 0, discount: 0, shipping: 0, tax: 0, total: 0,
  coupon: null, readyForCheckout: false,
};

export const toCartItems = (basket) => (basket?.lines ?? []).map((line) => ({
  id: line.productId,
  variantId: line.variantId,
  name: line.name,
  translations: line.translations,
  imageUrl: line.imageUrl,
  price: line.unitPrice,
  qty: line.quantity,
  lineTotal: line.lineTotal,
  currency: basket.currency,
  sellable: line.sellable,
  available: line.available,
  // وصف المتغيّر الحيّ من الخادم (V3): null لمنتج بلا خيارات.
  variantLabel: line.variantLabel ?? null,
}));

// مشكلة سطر تمنع الدفع: لم يعد متاحاً للبيع، أو كميته أكبر من المتاح الآن (السلة لا تحجز — ADR-0026).
export function lineProblem(item) {
  if (!item.sellable) return { code: 'unavailable' };
  if (item.qty > item.available) return { code: 'onlyLeft', count: item.available };
  return null;
}

export const hasProblems = (items) => items.some((item) => lineProblem(item) !== null);

// الكمية بعد زيادة/إنقاص، في حدود السطر. الصفر يعني الحذف (الخادم يحذف السطر عند الصفر).
export const nextQuantity = (item, delta) => Math.max(0, Math.min(item.qty + delta, MAX_QUANTITY));

// رسالة كوبون مرفوض (السلة تعرضه نتيجةً لا خطأً): الواجهة العربية تعرض رسالة الخادم (أدقّ: "انتهت صلاحية الكوبون")،
// وغيرها يترجم الرمز الثابت — كقاعدة client.js لأخطاء ProblemDetails.
export function couponProblemMessage(coupon, { translate, preferServerDetail }) {
  if (!coupon || coupon.applied) return null;
  return (preferServerDetail && coupon.message) || translate(coupon.errorCode) || coupon.message || coupon.errorCode;
}
