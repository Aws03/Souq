import { contrastRatio } from '../../../app/tenantModel';

// ============================================================================
// نموذج إعدادات المتجر — منطق خالص مُختبَر: حالة النموذج من إعدادات الخادم، وجسم PUT كما يقبله،
// والمشكلات التي تمنع الحفظ مربوطةً بحقولها.
//
// لماذا فحص مسبق والخادم يفحص؟ لأن كل قواعد الإعدادات تصل من الخادم برمز واحد
// (InvalidTenantOperation) ورسالة عربية: تاجرٌ بالإنجليزية كان سيقرأ "هذا الإجراء غير مسموح"
// لأيّ خطأ — لونٍ غير مقروء أو رابطٍ على غير نطاقه أو عنوانٍ أطول بحرف. الفحص هنا يقول *أيّ*
// حقل و*لماذا* بلغته، بقيمٍ يقرؤها من الخادم (options) لا من نسخة هنا. الخادم يبقى الحَكَم.
// ============================================================================

const WHITE = '#FFFFFF';
const HEX = /^#[0-9a-f]{6}$/i;
const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
const PHONE = /^\+?[0-9 ()-]{6,30}$/;
const TIME_ZONE = /^(?:UTC|[A-Za-z_]+(?:\/[A-Za-z0-9_+-]+)+)$/;

export const COLOR_FIELDS = ['primary', 'secondary', 'accent', 'background', 'text'];

const texts = (dictionary, cultures) =>
  Object.fromEntries(cultures.map((culture) => [culture, dictionary?.[culture] ?? '']));

export function settingsToForm(settings, options) {
  const cultures = options?.cultures ?? Object.keys(settings?.displayName ?? {});
  const branding = settings?.branding ?? {};
  return {
    displayName: texts(settings?.displayName, cultures),
    defaultCulture: settings?.locale?.defaultCulture ?? cultures[0] ?? '',
    enabledCultures: [...(settings?.locale?.enabledCultures ?? [])],
    timeZone: settings?.locale?.timeZone ?? 'UTC',
    colors: Object.fromEntries(COLOR_FIELDS.map((field) => [field, (branding.colors?.[field] ?? '').toUpperCase()])),
    typography: branding.typography ?? '',
    themePreset: branding.themePreset ?? '',
    themeMode: branding.themeMode ?? 'system',
    openingEnabled: branding.opening?.enabled ?? false,
    openingStyle: branding.opening?.style ?? options?.openingStyles?.[0] ?? '',
    contactEmail: settings?.contact?.email ?? '',
    contactPhone: settings?.contact?.phone ?? '',
    address: texts(settings?.contact?.address, cultures),
    social: (settings?.social ?? []).map(({ network, url }) => ({ network, url })),
    seoTitle: texts(settings?.seo?.title, cultures),
    seoDescription: texts(settings?.seo?.description, cultures),
    announcement: texts(settings?.announcement, cultures),
  };
}

// نصّ فارغ يُرسل فارغاً فيحذفه الخادم (تعود الواجهة للغة الافتراضية) — لا يُسقَط هنا كي لا يبقى القديم.
const trimmed = (dictionary) =>
  Object.fromEntries(Object.entries(dictionary ?? {}).map(([culture, value]) => [culture, (value ?? '').trim()]));

export const buildSettingsPayload = (form) => ({
  displayName: trimmed(form.displayName),
  locale: {
    defaultCulture: form.defaultCulture,
    enabledCultures: form.enabledCultures,
    timeZone: form.timeZone.trim(),
  },
  branding: {
    colors: Object.fromEntries(COLOR_FIELDS.map((field) => [field, form.colors[field].trim().toUpperCase()])),
    typography: form.typography,
    themePreset: form.themePreset,
    themeMode: form.themeMode,
    opening: { enabled: form.openingEnabled, style: form.openingStyle },
  },
  contact: {
    email: form.contactEmail.trim() || null,
    phone: form.contactPhone.trim() || null,
    address: trimmed(form.address),
  },
  social: form.social
    .map(({ network, url }) => ({ network, url: url.trim() }))
    .filter(({ url }) => url),
  seo: { title: trimmed(form.seoTitle), description: trimmed(form.seoDescription) },
  announcement: trimmed(form.announcement),
});

// ── التباين ───────────────────────────────────────────────────────────────
// نصّ الزرّ كما يشتقّه Domain (BrandColors.ReadableOn): الأبيض أو لون نصّ المتجر، أيّهما أوضح.
// لا readableOn العامّة: تلك تقارن بأسود ثابت، والخادم يقارن بنصّ المتجر نفسه — فتفترقان على لون حدّي.
export const buttonTextOn = (background, text) =>
  (contrastRatio(background, WHITE) >= contrastRatio(background, text) ? WHITE : text);

// الفحوص الأربعة بترتيب رفض الخادم، بنسبها — تُعرض حيّةً بجانب اللوحة لا عند الحفظ فقط.
export function colorChecks(colors, contrast) {
  if (!COLOR_FIELDS.every((field) => HEX.test(colors?.[field] ?? ''))) return [];
  const { primary, accent, background, text } = colors;
  const check = (id, fields, a, b, minimum) => {
    const ratio = contrastRatio(a, b);
    return { id, fields, ratio, minimum, pass: ratio >= minimum };
  };
  return [
    check('text', ['text', 'background'], text, background, contrast.text),
    check('primaryButton', ['primary'], primary, buttonTextOn(primary, text), contrast.text),
    check('accentButton', ['accent'], accent, buttonTextOn(accent, text), contrast.text),
    check('primaryBackground', ['primary', 'background'], primary, background, contrast.ui),
  ];
}

// ── المشكلات ──────────────────────────────────────────────────────────────
// كل مشكلة: الحقل (لربط الرسالة به وبالتركيز عليه) ومفتاح ترجمتها وقيمها.
export function settingsProblems(form, options) {
  const problems = [];
  const add = (field, key, values = {}) => problems.push({ field, key, values });
  const { limits } = options;

  if (form.enabledCultures.length === 0) add('enabledCultures', 'languagesRequired');
  else if (!form.enabledCultures.includes(form.defaultCulture)) add('enabledCultures', 'defaultLanguageDisabled');

  const zone = form.timeZone.trim();
  if (!zone || zone.length > limits.timeZone || !TIME_ZONE.test(zone)) add('timeZone', 'timeZoneInvalid');

  for (const field of COLOR_FIELDS) {
    if (!HEX.test(form.colors[field].trim())) add(`colors.${field}`, 'colorInvalid');
  }
  for (const failed of colorChecks(form.colors, options.contrast).filter((c) => !c.pass)) {
    add(`colors.${failed.fields[0]}`, `contrast.${failed.id}`, {
      ratio: failed.ratio.toFixed(2), minimum: failed.minimum,
    });
  }

  const lengths = [
    ['displayName', limits.displayName],
    ['address', limits.address],
    ['seoTitle', limits.seoTitle],
    ['seoDescription', limits.seoDescription],
    ['announcement', limits.announcement],
  ];
  for (const [field, max] of lengths) {
    for (const [culture, value] of Object.entries(form[field])) {
      if ((value ?? '').trim().length > max) add(`${field}.${culture}`, 'tooLong', { max });
    }
  }

  const email = form.contactEmail.trim();
  if (email && !EMAIL.test(email)) add('contactEmail', 'emailInvalid');
  const phone = form.contactPhone.trim();
  if (phone && !PHONE.test(phone)) add('contactPhone', 'phoneInvalid');

  const links = form.social.filter(({ url }) => url.trim());
  if (links.length > limits.socialLinks) add('social', 'tooManyLinks', { max: limits.socialLinks });
  const seen = new Set();
  form.social.forEach(({ network, url }, index) => {
    const value = url.trim();
    if (!value) return;
    if (seen.has(network)) add(`social.${index}`, 'duplicateNetwork');
    seen.add(network);
    const domains = options.socialNetworks.find((n) => n.network === network)?.domains ?? [];
    if (!linkOnDomains(value, domains, limits.socialUrl)) {
      add(`social.${index}`, 'socialInvalid', { domains: domains.join(' / ') });
    }
  });

  if (form.openingStyle && !options.openingStyles.includes(form.openingStyle)) add('openingStyle', 'openingInvalid');
  return problems;
}

function linkOnDomains(value, domains, maxLength) {
  if (value.length > maxLength) return false;
  let url;
  try { url = new URL(value); } catch { return false; }
  if (url.protocol !== 'https:') return false;
  const host = url.hostname.toLowerCase();
  return domains.some((domain) => host === domain || host.endsWith(`.${domain}`));
}

// مشكلة الحقل الأولى، لعرضها تحته.
export const problemFor = (problems, field) => problems.find((problem) => problem.field === field) ?? null;

// هل تغيّر شيء عن آخر حالة محفوظة؟ مقارنة الجسم المُرسَل لا النموذج: مسافة زائدة ليست تعديلاً.
export const hasChanges = (form, saved) =>
  JSON.stringify(buildSettingsPayload(form)) !== JSON.stringify(buildSettingsPayload(saved));

// الشكل الذي تقرؤه themeVariables — كي تعرض المعاينة ما سيُحفظ لا ما حُفظ.
// ونصّ الأزرار يُمرَّر مشتقّاً كما سيشتقّه الخادم، لا متروكاً لتقدير المعاينة.
export function previewBranding(form) {
  const colors = { ...form.colors };
  if (HEX.test(colors.primary) && HEX.test(colors.text)) colors.onPrimary = buttonTextOn(colors.primary, colors.text);
  if (HEX.test(colors.accent) && HEX.test(colors.text)) colors.onAccent = buttonTextOn(colors.accent, colors.text);
  return { colors, typography: form.typography };
}
