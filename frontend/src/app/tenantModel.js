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

// ============================================================================
// متغيّرات التصميم الدلالية من هوية المتجر — المكوّنات لا تقرأ غيرها (--color-*، --tenant-font-*).
// ألوان الحالة (نجاح، معلومة، خطر) تُشتقّ لتبقى مقروءة في الوضعين، وأساسها ثابت عبر كل المتاجر عمداً.
//
// ── لماذا الوضع (فاتح/داكن) مُدخَل للاشتقاق لا تجاوزاً في CSS؟ ─────────────
// applyStoreTheme يكتب هذه المتغيّرات **سطرياً على <html>**، والسطري يعلو كل قاعدة CSS. فلو
// عُرِّف الوضع الداكن كقاعدة `[data-theme="dark"] { --color-bg: … }` لَغلبته قيمةُ المتجر
// الفاتحة وبقيت الخلفية بيضاء. النتيجة الوحيدة المتّسقة أن يكون الوضع مُعاملاً هنا: مجموعة
// رموز واحدة تُشتقّ من (ألوان المتجر + الوضع)، ومصدر حقيقة واحد.
//
// والداكن ليس عكساً للفاتح: لون المتجر يبقى هويةً، لكن السطح يُبنى من رمادٍ بارد قريب منه،
// والنصّ يُختار بالتباين لا بالذوق — mutedText يضمن 4.5:1 في الوضعين معاً.
// ============================================================================
// ألوان الحالة الأساسية — ثابتة عبر المتاجر (لا متجر يُعيد تعريف "خطر")، ومشتقّة للوضع.
//
// **`warning` أُضيف في M10 (TD-28).** كان العنبر الوحيد بين الخمسة مكتوباً رقمين حرفيين في CSS
// (`#fdf3e2` خلفيةً و`#8a5a0e` نصّاً)، ولا يمرّ من هنا — فلا يُشتقّ للوضع الداكن كأخواته: شارةُ
// "بانتظار الدفع" كانت تبقى شبه بيضاء وسط سطحٍ داكن، ونصُّها الغامق عليها. ومرورُه من هنا يُصلح ذلك
// ويضمن تباينه في الوضعين معاً بالحساب لا بالذوق (readableAgainst أدناه).
const STATUS = { success: '#2E7D32', info: '#2A5DA8', warning: '#8A5A0E', danger: '#C4674E' };

const DARK_SURFACE = '#12161C';   // أساس السطح الداكن: رمادي بارد لا أسود (الأسود الصريح يُتعب العين ويُسطّح الظلال)
const DARK_TEXT_ON = '#ECEFF4';

export const THEME_MODES = ['light', 'dark'];

export function themeVariables(branding, mode = 'light') {
  const colors = branding?.colors ?? {};
  const primary = hex(colors.primary, NEUTRAL.primary);
  const accent = hex(colors.accent, NEUTRAL.accent);
  const type = TYPOGRAPHY[branding?.typography] ?? TYPOGRAPHY[DEFAULT_TYPOGRAPHY];
  const dark = mode === 'dark';

  // في الداكن: الخلفية والنصّ يُشتقّان، ولا يُقرآن من إعداد المتجر (إعداده فاتح بطبيعته).
  const background = dark
    ? mix(DARK_SURFACE, primary, 0.10)
    : hex(colors.background, NEUTRAL.background);
  const text = dark ? DARK_TEXT_ON : hex(colors.text, NEUTRAL.text);

  // السطح أفتح من الخلفية في الداكن (الارتفاع بالضوء لا بالظلّ) وأبيض في الفاتح.
  const surface = dark ? mix(background, '#FFFFFF', 0.07) : '#FFFFFF';

  // لون الهوية على خلفية داكنة قد يصير غير مقروء — يُفتَح حتى يبلغ 4.5:1 بدل أن يُترك باهتاً.
  // ── المرجع الأصعب ─────────────────────────────────────────────────────────
  // النصّ يُقرأ على الخلفية وعلى البطاقات معاً، والمقروء على الأصعب منهما مقروء على الآخر. في الفاتح الأصعب
  // الخلفية (البطاقة بيضاء أفتح منها)، وفي الداكن الأصعب البطاقة (مرفوعة بالضوء فهي أفتح). كان الاشتقاق يقيس
  // على الخلفية في الوضعين، فسقط النصّ الثانوي وروابط لون الهوية على بطاقات الوضع الداكن — وجده axe على صفحة
  // متجر في المنصّة، وينطبق على كل متجر داكن.
  const textReference = dark ? surface : background;
  const primaryReadable = dark ? readableAgainst(primary, textReference) : primary;
  const accentReadable = dark ? readableAgainst(accent, textReference) : accent;

  // ── والأصعب حقّاً هو السطح الثانوي، لا الخلفية (M10) ──────────────────────
  // النصّ الثانوي يُقرأ أيضاً على `--color-surface-alt` (أقسام، لصائق، أزرار الترتيب)، وهو **أغمق** من
  // الخلفية في الفاتح. فاشتقاقُه على الخلفية وحدها كان يُنتج 4.42:1 عليه — أسقطه axe على كل مسار في
  // واجهة المتجر حين فُعِّلت قاعدة التباين في M10. وهذه هي العلّة نفسها التي صُحِّحت لألوان الحالة
  // أعلاه (كانت تُقاس على الخلفية والسطح الناعم أغمق) — صُحِّحت هناك ولم تُصحَّح هنا.
  const surfaceAlt = dark ? mix(background, '#FFFFFF', 0.04) : mix(background, text, 0.05);
  const mutedReference = contrastRatio(text, surfaceAlt) < contrastRatio(text, textReference)
    ? surfaceAlt : textReference;

  // ── اللوحة المقلوبة ───────────────────────────────────────────────────────
  // التذييل، والرأسية، وشريط الإدارة الجانبي، وشريط الإعلان: أسطح داكنة عمداً بنصّ فاتح.
  // كانت تُبنى من `--color-primary` خلفيةً و`--color-bg` نصّاً — وهو صحيح في الفاتح فقط.
  // في الداكن ينقلب الرمزان معاً: الهوية تُفتَح لتبقى مقروءة على خلفية داكنة، والخلفية
  // تسودّ. فتصير اللوحة فاتحة بنصٍّ داكن: شريط الإدارة ظهر رمادياً باهتاً وعلامته لا تُقرأ.
  //
  // فلها رمزها المستقلّ: داكنة في الوضعين. في الفاتح هي لون الهوية نفسه (وهذا ما يجعل
  // كل متجر يبدو متجره)، وفي الداكن سطحٌ داكن مصبوغ بهويته لا أسود محايد.
  const panel = dark ? mix(DARK_SURFACE, primary, 0.22) : primary;

  // ── ألوان الحالة ──────────────────────────────────────────────────────────
  // تُقرأ غالباً على سطحها الناعم (شارة "تمّ التسليم")، لا على الخلفية. كانت تُحسب مقروءةً على
  // الخلفية وحدها، والسطح الناعم أغمق منها في الفاتح (أفتح في الداكن) — فسقط الأخضر على شارته إلى
  // 4.19:1. تُحسب الآن على السطح الناعم؛ وما يُقرأ عليه يُقرأ على الخلفية من باب أولى، لأنها أبعد عنه.
  const statusSoft = {
    success: mix(STATUS.success, background, dark ? 0.82 : 0.88),
    info: mix(STATUS.info, background, dark ? 0.82 : 0.88),
    warning: mix(STATUS.warning, background, dark ? 0.82 : 0.88),
    danger: mix(STATUS.danger, background, dark ? 0.82 : 0.88),
  };

  return {
    '--color-primary': primaryReadable,
    '--color-primary-strong': dark ? mix(primaryReadable, '#FFFFFF', 0.18) : mix(primary, '#000000', 0.25),
    // في الداكن يُشتقّ النصّ من اللون بعد تحويله لا من إعداد المتجر: التاجر ضبط onPrimary
    // مقابل هويته الفاتحة، وهنا صارت الهوية أفتح لتُقرأ على خلفية داكنة — فالأبيض المحفوظ
    // يصير أبيض على رمادي فاتح، وزرّ "أضف إلى السلّة" يبدو معطّلاً.
    '--color-on-primary': dark ? readableOn(primaryReadable) : hex(colors.onPrimary, readableOn(primary)),
    '--color-secondary': dark ? primaryReadable : hex(colors.secondary, primary),
    '--color-accent': accentReadable,
    '--color-accent-soft': mix(accentReadable, dark ? background : '#FFFFFF', 0.45),
    '--color-on-accent': dark ? readableOn(accentReadable) : hex(colors.onAccent, readableOn(accent)),
    '--color-panel': panel,
    '--color-panel-strong': dark ? mix(panel, '#000000', 0.25) : mix(primary, '#000000', 0.25),
    // النصّ على اللوحة: في الفاتح هو ما تحقّق Domain من قراءته على الهوية نفسها.
    '--color-on-panel': dark ? DARK_TEXT_ON : hex(colors.onPrimary, readableOn(primary)),
    // واللوحة داكنة دائماً، فلون التمييز عليها يُفتَح نحو الأبيض لا نحو الخلفية.
    '--color-accent-on-panel': readableAgainst(accent, panel),
    // ── ولون الهوية المميّز **نصّاً** على سطح عادي (M10) ───────────────────────
    // `--color-accent` يبقى لون العلامة كما اختاره التاجر: حدودٌ وحوافُّ تركيز وخطوطُ مخطّطات وخلفيّةُ
    // زرّ (بنصّها `--color-on-accent`). لكنّه كان يُستعمل **نصّاً** أيضاً — اسمُ المتجر في الرأسية
    // وصفحات الدخول — بلا أي فحص قراءة في الوضع الفاتح: ذهبُ متجر الاختبار على أبيض = 2.15:1، أي أنّ
    // **اسم المتجر نفسه** كان غير مقروء على كل صفحة. الفرع الداكن كان محروساً والفاتح لا، وهو تفاوتٌ
    // يشبه السهو. الحلّ هو شكلُ `--color-accent-on-panel` نفسه: رمزٌ منفصل للنصّ، فلا تتغيّر العلامة.
    // والمرجع هو الأصعب لا الأبيض: لنصٍّ غامق يكون الأبيض **أسهل** خلفية (أعلى إضاءة ⇒ أعلى نسبة)،
    // فالمقروء عليه لا يلزم أن يُقرأ على سطحٍ أغمق قليلاً — وهو ما أوقع صفحة 404 (خلفيّتها ليست بيضاء).
    // `mutedReference` أعلاه هو أصعب سطحٍ يقع عليه نصّ في هذا الوضع، فيُقاس عليه.
    '--color-accent-on-surface': readableAgainst(accent, mutedReference),

    '--color-bg': background,
    '--color-surface': surface,
    '--color-surface-alt': surfaceAlt,
    '--color-text': text,
    '--color-text-muted': mutedText(text, mutedReference),
    '--color-border': mix(background, text, dark ? 0.16 : 0.12),

    // ألوان الحالة تُشتقّ للوضع كي تبقى مقروءة: الأخضر الداكن على خلفية داكنة يختفي.
    '--color-success': readableAgainst(STATUS.success, statusSoft.success),
    '--color-success-soft': statusSoft.success,
    '--color-info': readableAgainst(STATUS.info, statusSoft.info),
    '--color-info-soft': statusSoft.info,
    '--color-warning': readableAgainst(STATUS.warning, statusSoft.warning),
    '--color-warning-soft': statusSoft.warning,
    '--color-danger': readableAgainst(STATUS.danger, statusSoft.danger),
    '--color-danger-soft': statusSoft.danger,

    // الظلّ لا يعمل على سطح داكن: الارتفاع هناك حدٌّ مضيء لا ظلّ أسود.
    '--shadow': dark ? `0 1px 0 ${rgba('#FFFFFF', 0.06)}, 0 8px 24px ${rgba('#000000', 0.45)}`
      : `0 6px 24px ${rgba(primary, 0.08)}`,
    '--shadow-lg': dark ? `0 24px 60px ${rgba('#000000', 0.65)}` : `0 20px 50px ${rgba('#000000', 0.25)}`,

    '--tenant-font-heading': type.heading,
    '--tenant-font-body': type.body,
  };
}

// يفتح اللون تدريجياً حتى يبلغ 4.5:1 على الخلفية، فيبقى هو نفسه ما لم يكن غير مقروء أصلاً.
export function readableAgainst(color, background, target = 4.5) {
  if (contrastRatio(color, background) >= target) return color;
  const towards = contrastRatio('#FFFFFF', background) > contrastRatio('#000000', background) ? '#FFFFFF' : '#000000';
  for (let weight = 0.1; weight <= 1; weight += 0.05) {
    const candidate = mix(color, towards, weight);
    if (contrastRatio(candidate, background) >= target) return candidate;
  }
  return towards;
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
let storeCurrencyDecimals = null;
export const setStoreCurrency = (code, decimals = null) => {
  storeCurrency = typeof code === 'string' ? code.toUpperCase() : '';
  storeCurrencyDecimals = Number.isInteger(decimals) ? decimals : null;
};
export const getStoreCurrency = () => storeCurrency;

// خانات العملة الصغرى.
//
// **قيمة الخادم أوّلاً، وIntl احتياطاً.** الخادم يُرسل `locale.currencyDecimals` (من
// `CurrencyInfo.MinorUnits`، وهي المرجع الذي يُقرَّب به كل مبلغ في النظام) — وكان هذا الحقل
// يُرسَل ويُختبَر **ولا يقرأه أحد**: الواجهة تشتقّ الخانات من جدول ISO في متصفّح الزائر.
// الجدولان يتّفقان اليوم، وهذا بالضبط ما يجعل الاختلاف خطيراً حين يقع: متصفّح قديم أو عملة
// تغيّرت خاناتها تجعل الزبون يقرأ مبلغاً بدقّة تخالف ما حسبه الخادم، بلا خطأ في أي مكان.
// حقلٌ مشحون لا يقرأه أحد ليس توثيقاً زائداً، بل انحرافٌ ينتظر (M19).
export function currencyDecimals(currency) {
  const code = typeof currency === 'string' ? currency.toUpperCase() : '';
  if (storeCurrencyDecimals !== null && code === storeCurrency) return storeCurrencyDecimals;
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

// المتجر المغلق يُخدَم إعدادُه (R-08): الخادم يردّ 200 بهويّته وحالته كي تُعرض شاشة الإغلاق بهويّة المتجر لا صفحة
// خطأ عارية — فلم يعد الخطأ وحده إشارةَ الإغلاق. إعداد بلا حالة (خادم أقدم) يُعتبر مفتوحاً كما كان.
const OPEN_STATUS = 'Active';

// ============================================================================
// **التطبيق يُركَّب، والواجهة قد تكون مغلقة: أمران مختلفان صارا كذلك في C3.**
//
// قبله كان `Active` وحدها تُركّب التطبيق، وما عداها شاشةً تحلّ محلّ التطبيق كلّه — فصاحب متجرٍ
// موقوف لم يكن يستطيع الوصول إلى لوحته في متصفّحه، مع أن الخادم يسمح له. وقرار المالك
// C-17 = B هو «إدارة فقط»، فالمنع كان في المتصفّح لا في الخادم، وهو أسوأ موضعٍ للمنع: يبدو
// عطلاً لا سياسة.
//
// فالآن: **المؤرشف وحده** يحلّ محلّ التطبيق (نهائي، لا لوحة ولا تتبّع). والموقوف وقيد التجهيز
// يُركَّبان — تعمل فيهما اللوحة والدخول، ويرى المتسوّق إشعاراً بهويّة المتجر مكان الواجهة.
// وهذا يطابق ما يفعله الخادم بالضبط (`TenantAvailabilityMiddleware.IsOpen`)، ومطابقتُه هي
// المقصود: حارسُ الواجهة تجربةٌ لا حماية، فإن خالف الخادمَ صار كذباً على أحد الطرفين.
// ============================================================================
const MOUNTABLE_STATUSES = new Set([OPEN_STATUS, 'Provisioning', 'Suspended']);

export const bootModeForConfig = (config) =>
  (MOUNTABLE_STATUSES.has(config?.status ?? OPEN_STATUS) ? 'store' : 'closed');

// هل واجهة المتجر مفتوحة للمتسوّق؟ (التسوّق والسلّة والدفع وحساب العميل.)
export const storefrontIsOpen = (config) => (config?.status ?? OPEN_STATUS) === OPEN_STATUS;

// حالة المتجر كمفتاح ترجمة للإشعار المعروض — ثلاث حالات، ثلاث رسائل. كانت الثلاث رسالةً واحدة.
export const storeStatusKey = (config) => {
  const status = config?.status ?? OPEN_STATUS;
  return MOUNTABLE_STATUSES.has(status) || status === 'Archived' ? status.toLowerCase() : 'closed';
};
