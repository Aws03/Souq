import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';
import { dateLocale, dateOptions, getStoreCulture } from '../app/dateLocale';

// ============================================================================
// تهيئة i18next مرة واحدة عند إقلاع التطبيق. اللغة المحفوظة في localStorage
// لها الأولوية (اختيار المستخدم صريح)، وإلا نكتشفها من لغة المتصفح، وإلا
// نفترض العربية (هوية المتجر الأساسية).
// ============================================================================
const STORAGE_KEY = 'souq_lang';
const SUPPORTED = ['ar', 'en'];

// ============================================================================
// حزمة الترجمة تُحمَّل للغة الزائر وحدها.
//
// كانت اللغتان تُستوردان استيراداً ساكناً، فتُجمَّعان في أوّل حزمة تصل المتصفّح: ١٥٤ كيلوبايت
// (٥٣ مضغوطة) — أكبر من react-dom نفسها، ونصفها لغة لا يقرؤها هذا الزائر إطلاقاً.
//
// الاستيراد الديناميكي يجعل كلاً منهما حزمة مستقلّة، فلا تصل إلّا حين تُطلب: عند الإقلاع
// للغة المختارة، وعند التبديل للأخرى. الثمن انتظارٌ قصير قبل أول رسم — وهو ما كان يقع
// أصلاً، لكن للغتين معاً.
// ============================================================================
const BUNDLES = {
  ar: () => import('./locales/ar.json'),
  en: () => import('./locales/en.json'),
};

async function ensureLanguage(language) {
  if (i18n.hasResourceBundle?.(language, 'translation')) return;
  const module = await BUNDLES[language]();
  i18n.addResourceBundle(language, 'translation', module.default, true, true);
}

function detectLanguage() {
  const stored = localStorage.getItem(STORAGE_KEY);
  if (SUPPORTED.includes(stored)) return stored;
  const browserLang = navigator.language?.slice(0, 2);
  return SUPPORTED.includes(browserLang) ? browserLang : 'ar';
}

const initialLanguage = detectLanguage();

function applyDocumentDirection(lang) {
  document.documentElement.dir = lang === 'ar' ? 'rtl' : 'ltr';
  document.documentElement.lang = lang;
}

// ============================================================================
// عزل الاتجاه (bidi) لِما يكتبه التاجر.
//
// اسم منتج عربي داخل جملة إنجليزية — أو العكس — يُحسب اتجاهه من محيطه لا من نفسه، فتنزلق
// علامات الترقيم المجاورة إلى الطرف الخطأ: "(كوب حراري)." تُرسم ".(كوب حراري)". ليس خطأ
// خطّ ولا ترجمة، بل خوارزمية bidi في يونيكود تفعل ما طُلب منها بالضبط.
//
// FSI…PDI يعزل المقطع: يُحسب اتجاهه من محتواه هو، ولا يمسّ ما حوله. والصياغة `{{name, bidi}}`
// تجعل ذلك مرئياً في ملفّ الترجمة نفسه — المترجم يرى أن هذا الموضع يستقبل نصّاً غريباً.
//
// لماذا لا نعزل كل شيء تلقائياً؟ لأن ما نكتبه نحن معروف الاتجاه سلفاً؛ ما يحتاج العزل هو
// ما يأتي من بيانات المتجر: اسم منتج، أو فئة، أو مستخدم.
// ============================================================================
const FIRST_STRONG_ISOLATE = '\u2068';
const POP_DIRECTIONAL_ISOLATE = '\u2069';

export const isolateBidi = (value) => {
  const text = value == null ? '' : String(value);
  return text === '' ? text : FIRST_STRONG_ISOLATE + text + POP_DIRECTIONAL_ISOLATE;
};

/**
 * وعد التهيئة. من يعرض نصّاً مترجَماً ينتظره أولاً — `main.jsx` قبل أول رسم، وإعداد
 * الاختبارات قبل أول عرض. الانتظار هنا استيراد حزمة واحدة لا رحلة شبكة إلى خادم.
 *
 * ولغة الاحتياط هي لغة الإقلاع نفسها لا 'ar' ثابتة: الإحالة إلى حزمة غير محمّلة تُخرج اسم
 * المفتاح للزبون. (وتطابق المفاتيح بين اللغتين يحرسه `translationKeys.test.js`.)
 */
export const i18nReady = (async () => {
  const initial = await BUNDLES[initialLanguage]();

  await i18n.use(initReactI18next).init({
    resources: { [initialLanguage]: { translation: initial.default } },
    lng: initialLanguage,
    fallbackLng: initialLanguage,
    interpolation: { escapeValue: false }, // React يهرّب المخرجات أصلاً — لا حاجة لتكرار ذلك هنا
  });

  // التسجيل بعد init: i18next 26 يبني خدمة المنسّقات أثناء التهيئة، فالإضافة تأتي بعدها.
  i18n.services.formatter.add('bidi', (value) => isolateBidi(value));

  applyDocumentDirection(initialLanguage);
  return i18n;
})();

// نقطة الدخول الوحيدة لتبديل اللغة: تُحدّث i18next، تحفظ الاختيار، وتضبط
// اتجاه الصفحة (dir) ولغتها (lang) على عنصر <html> فوراً.
export async function setLanguage(lang) {
  if (!SUPPORTED.includes(lang)) return;
  // الحزمة أولاً: تبديلٌ قبل وصولها يعرض أسماء المفاتيح للحظة.
  await ensureLanguage(lang);
  await i18n.changeLanguage(lang);
  localStorage.setItem(STORAGE_KEY, lang);
  applyDocumentDirection(lang);
}

// تهيئة التاريخ — الأماكن التي تعرض تاريخاً (تقييمات، كوبونات، طلبات) تستدعي هذه بدل تكرار
// منطق اللغة/التقويم في كل مكوّن. الموضع والمنطقة الزمنية من إعداد المتجر لا من ثابت مكتوب
// (app/dateLocale.js يشرح القاعدة، وTenantProvider يضبطهما عند الإقلاع).
export function formatDate(iso) {
  return new Date(iso).toLocaleDateString(dateLocale(i18n.language, getStoreCulture()), dateOptions());
}

// تاريخ + وقت معاً (خط زمني تتبّع الطلب) — نفس القاعدة.
export function formatDateTime(iso) {
  return new Date(iso).toLocaleString(
    dateLocale(i18n.language, getStoreCulture()),
    dateOptions({ dateStyle: 'medium', timeStyle: 'short' }));
}

export default i18n;
