import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';
import ar from './locales/ar.json';
import en from './locales/en.json';

// ============================================================================
// تهيئة i18next مرة واحدة عند إقلاع التطبيق. اللغة المحفوظة في localStorage
// لها الأولوية (اختيار المستخدم صريح)، وإلا نكتشفها من لغة المتصفح، وإلا
// نفترض العربية (هوية المتجر الأساسية).
// ============================================================================
const STORAGE_KEY = 'souq_lang';
const SUPPORTED = ['ar', 'en'];

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

i18n.use(initReactI18next).init({
  resources: {
    ar: { translation: ar },
    en: { translation: en },
  },
  lng: initialLanguage,
  fallbackLng: 'ar',
  interpolation: { escapeValue: false }, // React يهرّب المخرجات أصلاً — لا حاجة لتكرار ذلك هنا
});

applyDocumentDirection(initialLanguage);

// نقطة الدخول الوحيدة لتبديل اللغة: تُحدّث i18next، تحفظ الاختيار، وتضبط
// اتجاه الصفحة (dir) ولغتها (lang) على عنصر <html> فوراً.
export function setLanguage(lang) {
  if (!SUPPORTED.includes(lang)) return;
  i18n.changeLanguage(lang);
  localStorage.setItem(STORAGE_KEY, lang);
  applyDocumentDirection(lang);
}

// تهيئة التاريخ حسب اللغة الحالية — الأماكن التي تعرض تاريخاً (تقييمات،
// كوبونات، طلبات) تستدعي هذه بدل تكرار منطق اللغة/التقويم في كل مكوّن.
export function formatDate(iso) {
  const locale = i18n.language === 'ar' ? 'ar-JO' : 'en-US';
  return new Date(iso).toLocaleDateString(locale);
}

export default i18n;
