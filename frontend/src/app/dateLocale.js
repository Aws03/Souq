// ============================================================================
// موضع التاريخ ومنطقته الزمنية (المرحلة 16) — منطق خالص مُختبَر.
//
// كانت الواجهة تكتب 'ar-JO' أو 'en-US' حرفياً: كل متجر على المنصّة يعرض تواريخه بعُرف الأردن
// أو الولايات المتحدة مهما كان إعداده، والمنطقة الزمنية كانت منطقة *متصفّح الزائر* — فالطلب
// الذي سجّله المتجر الساعة 4 يظهر لزبون في بلد آخر الساعة 2، ويختلف عمّا يراه التاجر لنفس الطلب.
//
// القاعدة: اللغة من الزائر (هو من يقرأ)، والإقليم والمنطقة الزمنية من المتجر (هو من يسجّل).
// متجر ثقافته 'ar-SA' وزائر يقرأ الإنجليزية ⇒ 'en-SA': أرقام إنجليزية وتقويم المتجر.
// ============================================================================
export function dateLocale(uiLanguage, storeCulture) {
  const language = String(uiLanguage || '').split('-')[0] || 'en';
  const region = String(storeCulture || '').split('-')[1];
  return region ? `${language}-${region}` : language;
}

let storeCulture = '';
let storeTimeZone = '';

export function setStoreDateSettings({ culture, timeZone } = {}) {
  storeCulture = typeof culture === 'string' ? culture : '';
  storeTimeZone = typeof timeZone === 'string' ? timeZone : '';
}

export const getStoreCulture = () => storeCulture;

// منطقة زمنية غير صالحة تُسقط Intl بأكملها — والتاريخ ليس ما يستحقّ إسقاط صفحة.
// منطقة المتصفّح تراجعٌ مرئي في العرض، لا خطأ صامت.
export function dateOptions(base = {}) {
  if (!storeTimeZone) return base;
  try {
    new Intl.DateTimeFormat('en', { timeZone: storeTimeZone });
    return { ...base, timeZone: storeTimeZone };
  } catch {
    return base;
  }
}
