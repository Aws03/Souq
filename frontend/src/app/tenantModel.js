// ============================================================================
// الواجهة البيضاء في وقت التشغيل (المرحلة 15، WhiteLabel.md §3، ADR-0035) — منطق خالص من إعداد المتجر
// (GET /api/storefront/config): متغيّرات التصميم الدلالية من ألوانه وخطّه، اسمه ونصوصه بلغة الزائر، تنسيق المال بعملته وخاناتها
// الصغرى، ووحداته المفعّلة. البناء نفسه يرسم أيّ متجر — لا اسم ولا عملة ولا لون مكتوب لمتجر بعينه. مُختبَر بـ Vitest.
// ============================================================================

// قيم محايدة لحقل ناقص — ليست هوية متجر.
const NEUTRAL = { primary: '#1F2937', accent: '#D97706', background: '#F9FAFB', text: '#111827' };
const DARK_TEXT = '#111827';
const LIGHT_TEXT = '#FFFFFF';

// خطوط المتجر (BrandPresets.Typography في الخادم). اللاتينية (واجهة إنجليزية) بخطّ Inter دائماً — styles.css.
export const TYPOGRAPHY = {
  'kufi-tajawal': { heading: "'Reem Kufi', 'Tajawal', sans-serif", body: "'Tajawal', sans-serif", families: ['Reem+Kufi:wght@500;600;700', 'Tajawal:wght@400;500;700'] },
  tajawal: { heading: "'Tajawal', sans-serif", body: "'Tajawal', sans-serif", families: ['Tajawal:wght@400;500;700'] },
  cairo: { heading: "'Cairo', sans-serif", body: "'Cairo', sans-serif", families: ['Cairo:wght@400;600;700'] },
  almarai: { heading: "'Almarai', sans-serif", body: "'Almarai', sans-serif", families: ['Almarai:wght@400;700'] },
  'ibm-plex': { heading: "'IBM Plex Sans Arabic', sans-serif", body: "'IBM Plex Sans Arabic', sans-serif", families: ['IBM+Plex+Sans+Arabic:wght@400;500;700'] },
};
const DEFAULT_TYPOGRAPHY = 'tajawal';

export const fontStylesheetUrl = (typography) => {
  const families = [...(TYPOGRAPHY[typography] ?? TYPOGRAPHY[DEFAULT_TYPOGRAPHY]).families, 'Inter:wght@400;500;600;700'];
  return `https://fonts.googleapis.com/css2?${families.map((f) => `family=${f}`).join('&')}&display=swap`;
};

// ── الألوان ────────────────────────────────────────────────────────────────

const HEX = /^#[0-9a-f]{6}$/i;
export const hex = (value, fallback) => (HEX.test(value ?? '') ? value.toUpperCase() : fallback);

const toRgb = (color) => {
  const n = Number.parseInt(color.slice(1), 16);
  return [(n >> 16) & 255, (n >> 8) & 255, n & 255];
};
const toHex = (rgb) => `#${rgb.map((c) => Math.round(c).toString(16).padStart(2, '0')).join('').toUpperCase()}`;

// weight: 0 ⇒ a كما هو، 1 ⇒ b.
export const mix = (a, b, weight) => {
  const [x, y] = [toRgb(a), toRgb(b)];
  return toHex(x.map((c, i) => c + (y[i] - c) * weight));
};

const rgba = (color, alpha) => `rgba(${toRgb(color).join(', ')}, ${alpha})`;

// نسبة التباين (WCAG 2.1).
export function contrastRatio(a, b) {
  const luminance = (color) => {
    const [r, g, bl] = toRgb(color).map((c) => {
      const s = c / 255;
      return s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4;
    });
    return 0.2126 * r + 0.7152 * g + 0.0722 * bl;
  };
  const [l1, l2] = [luminance(a), luminance(b)].sort((m, n) => n - m);
  return (l1 + 0.05) / (l2 + 0.05);
}

export const readableOn = (color) => (contrastRatio(color, LIGHT_TEXT) >= contrastRatio(color, DARK_TEXT) ? LIGHT_TEXT : DARK_TEXT);

// نصّ ثانوي أخفّ من النصّ لكن مقروء (WCAG AA 4.5:1 على الخلفية) — أخفّ درجة تحقّق ذلك.
export function mutedText(text, background) {
  for (let weight = 0.45; weight > 0; weight -= 0.05) {
    const candidate = mix(text, background, weight);
    if (contrastRatio(candidate, background) >= 4.5) return candidate;
  }
  return text;
}

// متغيّرات التصميم الدلالية من هوية المتجر — المكوّنات لا تقرأ غيرها (--color-*، --tenant-font-*). ألوان الحالة (نجاح، معلومة،
// خطر) ثابتة عبر كل المتاجر عمداً (styles.css).
export function themeVariables(branding) {
  const colors = branding?.colors ?? {};
  const primary = hex(colors.primary, NEUTRAL.primary);
  const accent = hex(colors.accent, NEUTRAL.accent);
  const background = hex(colors.background, NEUTRAL.background);
  const text = hex(colors.text, NEUTRAL.text);
  const type = TYPOGRAPHY[branding?.typography] ?? TYPOGRAPHY[DEFAULT_TYPOGRAPHY];
  return {
    '--color-primary': primary,
    '--color-primary-strong': mix(primary, '#000000', 0.25),
    '--color-on-primary': hex(colors.onPrimary, readableOn(primary)),
    '--color-secondary': hex(colors.secondary, primary),
    '--color-accent': accent,
    '--color-accent-soft': mix(accent, '#FFFFFF', 0.45),
    '--color-on-accent': hex(colors.onAccent, readableOn(accent)),
    '--color-bg': background,
    '--color-surface-alt': mix(background, text, 0.05),
    '--color-text': text,
    '--color-text-muted': mutedText(text, background),
    '--color-border': mix(background, text, 0.12),
    '--shadow': `0 6px 24px ${rgba(primary, 0.08)}`,
    '--tenant-font-heading': type.heading,
    '--tenant-font-body': type.body,
  };
}

// ── النصوص واللغات ─────────────────────────────────────────────────────────

const defaultCulture = (config) => config?.settings?.locale?.defaultCulture;

// نصّ بلغة الزائر، وإلا لغة المتجر الافتراضية، وإلا أوّل نصّ موجود.
export function pickText(dictionary, language, fallbackCulture) {
  if (!dictionary) return '';
  return dictionary[language] || (fallbackCulture && dictionary[fallbackCulture]) || Object.values(dictionary).find(Boolean) || '';
}

export const storeName = (config, language) =>
  pickText(config?.settings?.displayName, language, defaultCulture(config)) || config?.name || '';

export const documentTitle = (config, language) =>
  pickText(config?.settings?.seo?.title, language, defaultCulture(config)) || storeName(config, language);

export const documentDescription = (config, language) =>
  pickText(config?.settings?.seo?.description, language, defaultCulture(config));

export const announcementText = (config, language) =>
  pickText(config?.settings?.announcement, language, defaultCulture(config));

export const enabledLanguages = (config) => config?.settings?.locale?.enabledCultures ?? [];

// لغة الزائر إن فعّلها المتجر، وإلا لغته الافتراضية.
export function supportedLanguage(config, language) {
  const enabled = enabledLanguages(config);
  return enabled.length === 0 || enabled.includes(language) ? language : (defaultCulture(config) ?? language);
}

// ── الوحدات ────────────────────────────────────────────────────────────────

// إخفاء واجهة الوحدة المعطّلة — تجربة لا حماية: الخادم يرفض نقاطها بـ 404 ModuleDisabled في كل الأحوال.
export const isModuleEnabled = (config, module) => (config?.modules ?? []).includes(module);

// ── المال ──────────────────────────────────────────────────────────────────

// عملة المتجر لأسعار بلا عملة صريحة (حدود فلتر السعر، مثلاً) — يضبطها TenantProvider عند الإقلاع.
let storeCurrency = '';
export const setStoreCurrency = (code) => { storeCurrency = typeof code === 'string' ? code.toUpperCase() : ''; };
export const getStoreCurrency = () => storeCurrency;

// خانات العملة الصغرى من Intl (ISO 4217) — لا جدول عملات مكتوب هنا.
export function currencyDecimals(currency) {
  try {
    return new Intl.NumberFormat('en', { style: 'currency', currency }).resolvedOptions().maximumFractionDigits;
  } catch {
    return 2;
  }
}

const formatters = new Map();

// مبلغ بعملته وخاناتها بلغة الزائر (أرقام لاتينية في الواجهتين). عملة غير صالحة ⇒ رقم بخانتين.
export function formatMoney(amount, currency, language) {
  const value = Number(amount ?? 0);
  const code = typeof currency === 'string' ? currency.trim().toUpperCase() : '';
  if (!/^[A-Z]{3}$/.test(code)) return value.toFixed(2);

  const key = `${language}|${code}`;
  let formatter = formatters.get(key);
  if (!formatter) {
    const decimals = currencyDecimals(code);
    formatter = new Intl.NumberFormat(language === 'ar' ? 'ar-u-nu-latn' : 'en-u-nu-latn', {
      style: 'currency', currency: code, currencyDisplay: language === 'ar' ? 'symbol' : 'code',
      minimumFractionDigits: decimals, maximumFractionDigits: decimals,
    });
    formatters.set(key, formatter);
  }
  return formatter.format(value);
}

// ── الإقلاع ────────────────────────────────────────────────────────────────

// ردّ إعداد المتجر عند الإقلاع ⇒ الوضع: متجر موقوف (503 StoreUnavailable)، مضيف لا متجر عليه (404 StoreNotFound)، مضيف المنصّة
// (نقطة متجر على مضيف المنصّة ⇒ 404 NotFound)، أو خطأ شبكة يُعاد.
export function bootOutcome(error) {
  if (error?.status === 503 && error.code === 'StoreUnavailable') return 'closed';
  if (error?.status === 404 && error.code === 'StoreNotFound') return 'unknown';
  if (error?.status === 404) return 'platform';
  return 'error';
}
