import { describe, it, expect } from 'vitest';
import {
  buildSettingsPayload, buttonTextOn, colorChecks, hasChanges, previewBranding, problemFor, settingsProblems,
  settingsToForm,
} from './settingsForm';

// ============================================================================
// نموذج إعدادات المتجر. الخادم يفحص كل شيء ويرفض برمز واحد؛ ما يُختبر هنا أن الفحص المسبق:
//   • يطابق الخادم على الحدود (لا يرفض ما يقبله الخادم، ولا يمرّر ما يرفضه)،
//   • يربط كل مشكلة بحقلها،
//   • وأن الجسم المُرسَل لا يُسقط شيئاً بصمت (نصٌّ مُفرَّغ يجب أن يُرسل فارغاً ليُحذف).
// القيم أدناه هي ما تُرجعه نقطة الخيارات — تُمرَّر، لا تُفترض.
// ============================================================================
const options = {
  cultures: ['ar', 'en'],
  typography: ['kufi-tajawal', 'tajawal', 'cairo', 'almarai', 'ibm-plex'],
  themePresets: ['classic', 'minimal', 'bold'],
  themeModes: ['light', 'dark', 'system'],
  openingStyles: ['doors'],
  socialNetworks: [
    { network: 'instagram', domains: ['instagram.com'] },
    { network: 'x', domains: ['x.com', 'twitter.com'] },
  ],
  policyKinds: ['privacy', 'terms', 'returns', 'shipping', 'faq'],
  limits: {
    displayName: 80, announcement: 200, seoTitle: 70, seoDescription: 160, address: 200,
    socialLinks: 8, socialUrl: 300, timeZone: 64, brandingFileBytes: 2097152, policyUrl: 300,
  },
  contrast: { text: 4.5, ui: 3 },
};

const settings = {
  displayName: { ar: 'متجر', en: 'Store' },
  locale: { defaultCulture: 'ar', enabledCultures: ['ar', 'en'], timeZone: 'Asia/Amman', currency: 'JOD', currencyDecimals: 3 },
  branding: {
    colors: { primary: '#1e3a5f', secondary: '#E9EEF3', accent: '#F2A541', background: '#FFFFFF', text: '#1F2933', onPrimary: '#FFFFFF', onAccent: '#1F2933' },
    typography: 'tajawal', themePreset: 'classic', themeMode: 'system', opening: { enabled: false, style: 'doors' },
    logoUrl: null, faviconUrl: null, socialImageUrl: null,
  },
  contact: { email: 'hello@store.test', phone: null, address: { ar: 'عمّان' } },
  social: [{ network: 'instagram', url: 'https://instagram.com/store' }],
  seo: { title: {}, description: {} },
  announcement: {},
};

const form = (patch = {}) => ({ ...settingsToForm(settings, options), ...patch });
const keys = (f) => settingsProblems(f, options).map((p) => `${p.field}:${p.key}`);

describe('من الخادم إلى النموذج وبالعكس', () => {
  it('الإعدادات المحفوظة تعود كما هي بلا مشكلات', () => {
    expect(settingsProblems(form(), options)).toEqual([]);
    const payload = buildSettingsPayload(form());
    expect(payload.branding.colors.primary).toBe('#1E3A5F');
    expect(payload.contact).toEqual({ email: 'hello@store.test', phone: null, address: { ar: 'عمّان', en: '' } });
  });

  it('كل لغة من الخيارات لها خانة، ولو لم يُكتب فيها شيء بعد', () => {
    expect(form().seoTitle).toEqual({ ar: '', en: '' });
  });

  it('النصّ المُفرَّغ يُرسل فارغاً كي يحذفه الخادم لا كي يبقى القديم', () => {
    const f = form({ displayName: { ar: '   ', en: 'Store' } });
    expect(buildSettingsPayload(f).displayName).toEqual({ ar: '', en: 'Store' });
  });

  it('رابط تواصل فارغ لا يُرسل، فحذف الرابط يحذفه', () => {
    const f = form({ social: [{ network: 'instagram', url: '  ' }] });
    expect(buildSettingsPayload(f).social).toEqual([]);
  });

  it('لا تعديل حين يختلف النموذج بمسافة فقط', () => {
    const saved = form();
    expect(hasChanges(form({ contactEmail: ' hello@store.test ' }), saved)).toBe(false);
    expect(hasChanges(form({ themeMode: 'dark' }), saved)).toBe(true);
  });
});

describe('التباين كما يحسبه الخادم', () => {
  it('نصّ الزرّ الأبيض أو نصّ المتجر، أيّهما أوضح — لا أسود ثابت', () => {
    expect(buttonTextOn('#1E3A5F', '#1F2933')).toBe('#FFFFFF');
    expect(buttonTextOn('#F2A541', '#1F2933')).toBe('#1F2933');
  });

  it('لوحة مقروءة تجتاز الفحوص الأربعة', () => {
    const checks = colorChecks(form().colors, options.contrast);
    expect(checks.map((c) => c.id)).toEqual(['text', 'primaryButton', 'accentButton', 'primaryBackground']);
    expect(checks.every((c) => c.pass)).toBe(true);
  });

  it('نصّ رمادي فاتح على أبيض يُرفض بنسبته، مربوطاً بحقل النصّ', () => {
    const f = form({ colors: { ...form().colors, text: '#CCCCCC' } });
    const problem = problemFor(settingsProblems(f, options), 'colors.text');
    expect(problem.key).toBe('contrast.text');
    expect(Number(problem.values.ratio)).toBeLessThan(4.5);
  });

  it('أساسي فاتح لا يتميّز عن الخلفية يُرفض بعتبة الواجهة (3:1) لا بعتبة النصّ', () => {
    const f = form({ colors: { ...form().colors, primary: '#FAFAFA' } });
    expect(keys(f)).toContain('colors.primary:contrast.primaryBackground');
    expect(problemFor(settingsProblems(f, options), 'colors.primary').values.minimum).toBe(3);
  });

  it('العتبة من الخادم: رفعها يرفض ما كان مقبولاً', () => {
    const strict = { ...options, contrast: { text: 21, ui: 3 } };
    expect(settingsProblems(form(), strict).length).toBeGreaterThan(0);
  });

  it('لون ليس hex لا يُحسب تباينه، ويُبلَّغ عنه بخطئه هو', () => {
    const f = form({ colors: { ...form().colors, accent: 'orange' } });
    expect(keys(f)).toEqual(['colors.accent:colorInvalid']);
  });

  it('المعاينة تشتقّ نصّ الأزرار كما سيُحفظ', () => {
    const branding = previewBranding(form({ colors: { ...form().colors, accent: '#F2A541' } }));
    expect(branding.colors.onAccent).toBe('#1F2933');
  });
});

describe('الحدود والقوائم من الخادم', () => {
  it('الحدّ نفسه مقبول، وحرفٌ فوقه مرفوض بلغته', () => {
    expect(keys(form({ seoTitle: { ar: 'a'.repeat(70), en: '' } }))).toEqual([]);
    expect(keys(form({ seoTitle: { ar: '', en: 'a'.repeat(71) } }))).toEqual(['seoTitle.en:tooLong']);
  });

  it('اللغة الافتراضية يجب أن تبقى مفعّلة', () => {
    expect(keys(form({ enabledCultures: ['en'] }))).toEqual(['enabledCultures:defaultLanguageDisabled']);
    expect(keys(form({ enabledCultures: [] }))).toEqual(['enabledCultures:languagesRequired']);
  });

  it('منطقة زمنية بشكل IANA فقط', () => {
    expect(keys(form({ timeZone: 'America/Argentina/Buenos_Aires' }))).toEqual([]);
    expect(keys(form({ timeZone: 'GMT+3' }))).toEqual(['timeZone:timeZoneInvalid']);
  });

  it('رابط تواصل على غير نطاق شبكته، أو بلا https، يُرفض', () => {
    expect(keys(form({ social: [{ network: 'instagram', url: 'https://evil.test/instagram.com' }] })))
      .toEqual(['social.0:socialInvalid']);
    expect(keys(form({ social: [{ network: 'x', url: 'http://x.com/store' }] }))).toEqual(['social.0:socialInvalid']);
    expect(keys(form({ social: [{ network: 'x', url: 'https://mobile.twitter.com/store' }] }))).toEqual([]);
  });

  it('رابط واحد لكل شبكة', () => {
    const social = [{ network: 'x', url: 'https://x.com/a' }, { network: 'x', url: 'https://x.com/b' }];
    expect(keys(form({ social }))).toEqual(['social.1:duplicateNetwork']);
  });

  it('بريد وهاتف بشكلهما إن كُتبا، واختياريان إن لم يُكتبا', () => {
    expect(keys(form({ contactEmail: '', contactPhone: '' }))).toEqual([]);
    expect(keys(form({ contactEmail: 'not-an-email', contactPhone: 'call me' })))
      .toEqual(['contactEmail:emailInvalid', 'contactPhone:phoneInvalid']);
  });

  it('لا يشترط اسم عرض: الخادم لا يشترطه، والواجهة تعود لاسم المتجر', () => {
    expect(keys(form({ displayName: { ar: '', en: '' } }))).toEqual([]);
  });

  // TD-42: روابط السياسات — https مطلق وحده، وأيّ نطاق (التاجر يستضيف صفحته حيث يشاء).
  it('رابط سياسة يقبل أيّ نطاق https ويرفض ما ليس رابطاً مطلقاً', () => {
    expect(keys(form({ policies: { privacy: 'https://anywhere.test/privacy' } }))).toEqual([]);
    expect(keys(form({ policies: { privacy: 'http://anywhere.test/privacy' } }))).toEqual(['policies.privacy:policyInvalid']);
    expect(keys(form({ policies: { terms: '/pages/terms' } }))).toEqual(['policies.terms:policyInvalid']);
    expect(keys(form({ policies: { faq: 'example.test/faq' } }))).toEqual(['policies.faq:policyInvalid']);
  });

  it('السياسات كلها اختيارية، والفارغ يُرسل فارغاً ليحذفه الخادم', () => {
    const empty = { privacy: '', terms: '', returns: '', shipping: '', faq: '' };
    expect(keys(form({ policies: empty }))).toEqual([]);
    expect(buildSettingsPayload(form({ policies: { privacy: ' https://a.test/p ', terms: '' } })).policies)
      .toEqual({ privacy: 'https://a.test/p', terms: '' });
  });

  it('النموذج يحمل كل نوع سياسة يعلنه الخادم، ولو لم يضبط المتجر شيئاً', () => {
    expect(Object.keys(settingsToForm({ ...settings, policies: {} }, options).policies))
      .toEqual(options.policyKinds);
    expect(settingsToForm({ ...settings, policies: { faq: 'https://a.test/f' } }, options).policies.faq)
      .toBe('https://a.test/f');
  });
});
