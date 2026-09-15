// ============================================================================
// تجربة الافتتاح — منطق خالص مُختبَر: *هل* تُعرض، لا *كيف* تُرسم.
//
// الفكرة: متجرٌ قد يريد أن يفتح أبوابه لزائره أوّل مرّة — كشفٌ قصير يليق بمحلّ تجاري راقٍ.
// والخطر أن تصير حركةً إجبارية عامّة على كل متجر، وهو نقيض المقصود تماماً.
//
// لذلك القرار محكوم بخمسة شروط، وكلٌّ منها سبب حقيقي لا احتياط:
//
//  1) **المتجر فعّلها.** الافتراضي "لا". متجر لم يطلب شيئاً يفتح فوراً.
//  2) **الزائر لم يرها في هذه الجلسة.** تحفة أوّل مرّة تصير ضريبةً في المرّة الخامسة.
//     التخزين لكل جلسة (sessionStorage) لا للأبد: تبويب جديد بعد أسبوع تجربة جديدة،
//     وتنقّل داخل الموقع ليس كذلك.
//  3) **الزائر لا يطلب تقليل الحركة.** عندها لا كشف إطلاقاً — لا نسخة "أخفّ": الحركة نفسها
//     هي ما يُطلب تقليله، ومعناها (هذا متجر س) يصل من الصفحة التي تليها فوراً.
//  4) **الزائر جاء إلى الواجهة لا إلى رابط عميق.** من فتح رابط منتج بعينه يريد المنتج؛
//     وضع ستارة أمامه يؤخّر ما جاء من أجله ويضرّ بالفهرسة.
//  5) **ليس زاحف بحث.** لا فائدة منها له، وقد تُقاس كتأخير في العرض.
// ============================================================================
const SESSION_KEY = 'souq_opening_seen';

// الزواحف المعروفة — فحص خشن مقصود: خطؤه في الاتجاه الآمن (لا كشف) لا العكس.
const CRAWLER = /bot|crawl|spider|slurp|bingpreview|lighthouse|headless/i;

/** المسارات التي يجوز أن تسبقها تجربة الافتتاح: الواجهة وحدها. */
export const isOpeningEntryPath = (pathname) => pathname === '/' || pathname === '';

/**
 * @param {{enabled?: boolean, pathname?: string, seenThisSession?: boolean,
 *          prefersReducedMotion?: boolean, userAgent?: string}} input
 */
export function shouldPlayOpening({
  enabled = false,
  pathname = '/',
  seenThisSession = false,
  prefersReducedMotion = false,
  userAgent = '',
} = {}) {
  if (!enabled) return false;
  if (prefersReducedMotion) return false;
  if (seenThisSession) return false;
  if (!isOpeningEntryPath(pathname)) return false;
  if (CRAWLER.test(userAgent)) return false;
  return true;
}

export function hasSeenOpening() {
  try {
    return sessionStorage.getItem(SESSION_KEY) === '1';
  } catch {
    // تخزين محجوب: نعتبرها مرئية فلا تتكرّر الحركة عند كل تنقّل.
    return true;
  }
}

export function markOpeningSeen() {
  try {
    sessionStorage.setItem(SESSION_KEY, '1');
  } catch {
    /* محجوب: تُعرض مرّة واحدة في هذا التحميل على الأقل */
  }
}

export const prefersReducedMotion = () =>
  typeof window !== 'undefined'
  && typeof window.matchMedia === 'function'
  && window.matchMedia('(prefers-reduced-motion: reduce)').matches;

/**
 * الأنماط المعتمدة. "الأبواب" وحدها اليوم، والقائمة موجودة كي يكون إضافة نمطٍ ثانٍ
 * قراراً مرئياً لا سطراً في مكوّن — ونمط غير معروف يسقط إلى المعروف لا إلى شاشة سوداء.
 */
export const OPENING_STYLES = ['doors'];
export const DEFAULT_OPENING_STYLE = 'doors';
export const resolveOpeningStyle = (style) =>
  (OPENING_STYLES.includes(style) ? style : DEFAULT_OPENING_STYLE);

// مدّة الكشف كاملاً. قصيرة عمداً: ما يتجاوز ثانية ونصف أمام متجرٍ يريد الزبون تصفّحه عائق لا ترحيب.
export const OPENING_DURATION_MS = 1400;
