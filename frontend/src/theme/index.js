// ============================================================================
// نظام تبديل السمة — يتبع نفس نمط i18n/index.js تماماً: القيمة المحفوظة في
// localStorage تُطبَّق فوراً عند إقلاع التطبيق (قبل أي رسم)، وتُصدَّر دالة
// وحيدة (setTheme) كنقطة الدخول الوحيدة للتبديل. لا منطق ألوان في JS إطلاقاً —
// كل ما تفعله setTheme هو ضبط data-theme على <html>؛ الألوان نفسها معرَّفة في
// styles.css عبر [data-theme="..."] فقط.
// ============================================================================
const STORAGE_KEY = 'souq_theme';
export const THEMES = ['default', 'ocean', 'rose', 'forest'];

function detectTheme() {
  const stored = localStorage.getItem(STORAGE_KEY);
  return THEMES.includes(stored) ? stored : 'default';
}

function applyDocumentTheme(theme) {
  document.documentElement.dataset.theme = theme;
}

const initialTheme = detectTheme();
applyDocumentTheme(initialTheme);

export function getTheme() {
  return document.documentElement.dataset.theme || 'default';
}

export function setTheme(theme) {
  if (!THEMES.includes(theme)) return;
  localStorage.setItem(STORAGE_KEY, theme);
  applyDocumentTheme(theme);
}
