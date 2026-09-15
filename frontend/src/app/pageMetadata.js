// ============================================================================
// بيانات الصفحة الوصفية (SEO لكل مستأجر، المرحلة 16).
//
// المشكلة التي يحلّها: هوية المتجر كانت تُطبَّق مرّة عند الإقلاع، فكل صفحة في المتجر تحمل
// العنوان نفسه والوصف نفسه — صفحة المنتج، الفئة، البحث، السلّة. لمتجر تجاري هذا عطل
// اكتشاف حقيقي: لا صفحة منتج تُصنَّف على اسمها، ومشاركة أي رابط تعرض العنوان العام نفسه.
//
// الحساب هنا خالص وقابل للاختبار (كما tenantModel)، والأثر على المستند في usePageMetadata.
// لا شيء هنا يخترع بيانات: العنوان والوصف وصورة المشاركة كلّها من إعداد المتجر القائم.
// ============================================================================

// "اسم الصفحة — اسم المتجر"، بلا تكرار حين يكونان واحداً (الرئيسية).
export function pageTitle(pageName, storeTitle) {
  const page = (pageName ?? '').trim();
  const store = (storeTitle ?? '').trim();
  if (!page) return store;
  if (!store || page === store) return page || store;
  return `${page} — ${store}`;
}

// رابط قانوني بلا معاملات تتبّع ولا حالة عرض: الصفحة نفسها بمحتوى مختلف الترتيب ليست
// صفحة أخرى. يُبقي ما يغيّر المحتوى فعلاً (الفئة، البحث، رقم الصفحة).
const CANONICAL_KEEPS = ['cats', 'q', 'page'];

export function canonicalUrl(origin, pathname, search) {
  if (!origin) return null;
  const params = new URLSearchParams(search ?? '');
  const kept = new URLSearchParams();
  for (const key of CANONICAL_KEEPS) {
    const value = params.get(key);
    if (value) kept.set(key, value);
  }
  const query = kept.toString();
  return `${origin.replace(/\/$/, '')}${pathname}${query ? `?${query}` : ''}`;
}

// وسوم المشاركة الاجتماعية. صورة المتجر (socialImageUrl) هي الافتراضي، وصفحة المنتج
// تمرّر صورتها — فيظهر المنتج نفسه حين يُشارَك رابطه.
/**
 * @param {{title: string, description?: string, image?: string, url?: string, type?: string}} page
 */
export function socialTags({ title, description, image, url, type = 'website' }) {
  return [
    ['og:title', title],
    ['og:description', description],
    ['og:image', image],
    ['og:url', url],
    ['og:type', type],
    ['twitter:card', image ? 'summary_large_image' : 'summary'],
  ].filter(([, value]) => Boolean(value));
}

// الصفحات التي لا يجوز فهرستها: كل ما هو خاصّ بزائر بعينه أو بلا قيمة بحثية.
// المسارات التي لا تُفهرَس. /cart منها منذ المرحلة 16: صفحة سلّة مفهرسة نتيجة بحث فارغة لأي
// زائر غيره. القائمة نفسها يعكسها public/robots.txt — ويحرس اختبارٌ تطابقهما، لأن وسم
// noindex لا يراه إلّا زاحف ينفّذ JavaScript، بينما robots.txt يقرؤه الجميع.
export const PRIVATE_ROUTES = ['/account', '/orders', '/cart', '/checkout', '/confirmation', '/wishlist', '/track'];

export function robotsFor(pathname) {
  return PRIVATE_ROUTES.some((route) => pathname === route || pathname.startsWith(`${route}/`))
    ? 'noindex, nofollow'
    : null;
}
