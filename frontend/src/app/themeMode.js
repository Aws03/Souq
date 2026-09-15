// ============================================================================
// اختيار الوضع (فاتح/داكن) — منطق خالص مُختبَر.
//
// ثلاثة مصادر، وترتيبها مقصود:
//   1) اختيار الزائر الصريح (زرّ التبديل) — من يختار بنفسه لا يُنقَض اختياره.
//   2) تفضيل المتجر من إعداده (light / dark / system) — هوية المتجر لا ذوق المتصفّح.
//   3) تفضيل نظام الزائر (prefers-color-scheme) حين يقول المتجر "system" أو لا يقول شيئاً.
//
// ── لماذا التخزين لكل أصل لا لكل متجر؟ ─────────────────────────────────────
// لكل متجر مضيفه، ولكل مضيف تخزينه المحلّي المنفصل — فاختيار زائر في متجر لا يتسرّب إلى
// متجر آخر أصلاً. لا معرّف متجر في المفتاح، ولا شيء عن المتجر يُكتب في المتصفّح.
//
// والتخزين قد يكون محجوباً (نافذة خاصة، إعدادات صارمة): كل قراءة وكتابة محروسة، والفشل
// يعني "لا اختيار محفوظ" لا صفحة معطّلة.
// ============================================================================
const STORAGE_KEY = 'souq_theme';

export const THEME_MODES = ['light', 'dark'];
export const THEME_PREFERENCES = ['light', 'dark', 'system'];

const isMode = (value) => THEME_MODES.includes(value);

/**
 * @param {{stored?: string|null, storePreference?: string|null, systemPrefersDark?: boolean}} input
 * @returns {string} 'light' أو 'dark'
 */
export function resolveThemeMode({ stored, storePreference, systemPrefersDark = false } = {}) {
  if (isMode(stored)) return stored;
  if (isMode(storePreference)) return storePreference;
  // "system" صراحةً، أو إعداد غائب/غير معروف — كلّها تعني: اسأل المتصفّح.
  return systemPrefersDark ? 'dark' : 'light';
}

export const oppositeMode = (mode) => (mode === 'dark' ? 'light' : 'dark');

export function readStoredMode() {
  try {
    const value = localStorage.getItem(STORAGE_KEY);
    return isMode(value) ? value : null;
  } catch {
    return null;   // تخزين محجوب: لا اختيار محفوظ
  }
}

export function writeStoredMode(mode) {
  try {
    if (isMode(mode)) localStorage.setItem(STORAGE_KEY, mode);
  } catch {
    /* تخزين محجوب: الاختيار يعيش لهذه الجلسة وحدها */
  }
}

export function clearStoredMode() {
  try {
    localStorage.removeItem(STORAGE_KEY);
  } catch {
    /* لا شيء نفعله */
  }
}

// استعلام النظام كمصدر واحد — يُستدعى من المتصفّح فقط، فيبقى الملفّ قابلاً للاختبار بلا DOM.
export const systemPrefersDark = () =>
  typeof window !== 'undefined'
  && typeof window.matchMedia === 'function'
  && window.matchMedia('(prefers-color-scheme: dark)').matches;
