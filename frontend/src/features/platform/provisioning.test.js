import { describe, it, expect } from 'vitest';
import {
  activationWarnings, adminInviteProblems, adminState, buildIdentityPayload, canRemoveDomain, hostProblem,
  identityProblems, identityToForm, lifecycleActions, nextStep, previousStep, readiness, resumeStep, slugFromName,
} from './provisioning';

// ============================================================================
// تجهيز متجر من المنصّة. ما يُختبر: أن الفحص المسبق يطابق حدود الخادم الممرَّرة (لا يرفض ما يقبله الخادم ولا
// يمرّر ما يرفضه)، وأن الجاهزية قراءةٌ لا شرط، وأن الاستئناف يبدأ ممّا ينقص فعلاً، وأن إجراءات دورة الحياة
// المعروضة هي ما تسمح به قواعد Tenant.
// ============================================================================
const options = {
  settings: { cultures: ['ar', 'en'] },
  modules: ['promotions', 'reviews', 'wishlist'],
  statuses: ['Provisioning', 'Active', 'Suspended', 'Archived'],
  reservedSlugs: ['admin', 'api', 'app', 'platform', 'static', 'uploads', 'www'],
  limits: { nameMin: 2, nameMax: 100, slugMin: 2, slugMax: 40, timeZoneMax: 64, hostMax: 253, fullNameMax: 150, emailMax: 256 },
};

const valid = { name: 'Acme Home', slug: 'acme-home', currency: 'usd', defaultCulture: 'en', timeZone: 'America/New_York' };
const problems = (patch) => identityProblems({ ...valid, ...patch }, options);

describe('الهوية', () => {
  it('المعرّف يُقترح من الاسم اللاتيني، ولا يُخترع من اسم عربي', () => {
    expect(slugFromName('Acme Home & Garden!')).toBe('acme-home-garden');
    expect(slugFromName('Café Olé')).toBe('cafe-ole');
    expect(slugFromName('متجر الورد')).toBe('');
    expect(slugFromName('a'.repeat(39) + ' b', 40)).toBe('a'.repeat(39));
  });

  it('هوية صالحة بلا مشكلات، وتُرسل بعملة كبيرة ومعرّف صغير', () => {
    expect(problems({})).toEqual({});
    expect(buildIdentityPayload({ ...valid, slug: 'Acme-Home ' }))
      .toEqual({ name: 'Acme Home', slug: 'acme-home', currency: 'USD', defaultCulture: 'en', timeZone: 'America/New_York' });
  });

  it('الحدود من الخادم: الحدّ نفسه مقبول وما بعده مرفوض', () => {
    expect(problems({ slug: 'a'.repeat(40) })).toEqual({});
    expect(problems({ slug: 'a'.repeat(41) }).slug.key).toBe('slugInvalid');
    expect(problems({ name: 'A' }).name.key).toBe('nameLength');
  });

  it('صيغة المعرّف كما في Tenant: لا شرطة في طرف ولا شرطتان متتاليتان', () => {
    for (const slug of ['-acme', 'acme-', 'ac--me', 'Acme_Home', 'متجر']) expect(problems({ slug }).slug.key).toBe('slugInvalid');
  });

  it('المعرّفات المحجوزة تُقال باسمها قبل الإرسال', () => {
    expect(problems({ slug: 'admin' }).slug).toEqual({ key: 'slugReserved', values: { slug: 'admin' } });
  });

  it('العملة رمز ISO من ثلاثة أحرف؛ واللغة من لغات الخادم', () => {
    expect(problems({ currency: 'US' }).currency.key).toBe('currencyInvalid');
    expect(problems({ defaultCulture: 'fr' }).defaultCulture.key).toBe('cultureInvalid');
  });

  it('يبدأ بلا عملة مختارة: عملة المتجر قرار لا افتراض', () => {
    expect(identityProblems(identityToForm(), options).currency.key).toBe('currencyInvalid');
  });
});

describe('النطاق', () => {
  it('يُطبَّع كما يطبّعه الخادم', () => {
    expect(hostProblem('  Shop.Example.COM. ', options)).toBeNull();
  });

  it('المنفذ والمسار ليسا من النطاق', () => {
    expect(hostProblem('shop.localhost:5173', options).key).toBe('hostNoPort');
    expect(hostProblem('https://shop.example.com/', options).key).toBe('hostNoPort');
  });

  it('صيغة DNS، وبلا تكرار داخل المتجر نفسه', () => {
    expect(hostProblem('shop_example.com', options).key).toBe('hostInvalid');
    expect(hostProblem('SHOP.test', options, [{ host: 'shop.test' }]).key).toBe('hostDuplicate');
    expect(hostProblem('', options).key).toBe('hostRequired');
  });

  it('الأساسي لا يُحذف ما دام للمتجر غيره', () => {
    const domains = [{ host: 'a.test', isPrimary: true }, { host: 'b.test', isPrimary: false }];
    expect(canRemoveDomain(domains[0], domains)).toBe(false);
    expect(canRemoveDomain(domains[1], domains)).toBe(true);
    expect(canRemoveDomain(domains[0], [domains[0]])).toBe(true);
  });
});

describe('الجاهزية والاستئناف', () => {
  const store = (patch = {}) => ({ status: 'Provisioning', domains: [], ...patch });
  const admin = (patch = {}) => ({ role: 'TenantAdmin', status: 'Active', invitationPending: false, ...patch });

  it('حالة المدير: الموقوف لا يُحسب، والمعلّق دعوةٌ لا مدير', () => {
    expect(adminState([])).toBe('none');
    expect(adminState([admin({ invitationPending: true })])).toBe('invited');
    expect(adminState([admin({ status: 'Disabled' })])).toBe('none');
    expect(adminState([admin({ role: 'TenantStaff' })])).toBe('none');
    expect(adminState([admin({ invitationPending: true }), admin()])).toBe('active');
  });

  it('متجر جديد: لا نطاق، والمدير متعذّر قبله (الدعوة تحتاج نطاقاً)', () => {
    const ready = readiness(store(), []);
    expect(ready.items.map((i) => [i.id, i.state])).toEqual([
      ['domain', 'missing'], ['domainVerified', 'blocked'], ['admin', 'blocked'], ['status', 'missing'],
    ]);
  });

  it('متجر مكتمل وفعّال', () => {
    const ready = readiness(store({ status: 'Active', domains: [{ host: 'a.test', isPrimary: true, verifiedAt: '2026-09-17' }] }), [admin()]);
    expect(ready.items.every((i) => i.state === 'done')).toBe(true);
    expect(ready.primaryHost).toBe('a.test');
    expect(activationWarnings(ready)).toEqual([]);
  });

  it('التفعيل بلا نطاق ولا مدير يُنبَّه إليه ولا يُمنع', () => {
    expect(activationWarnings(readiness(store(), []))).toEqual(['domain', 'admin']);
  });

  it('الاستئناف من أوّل ما ينقص', () => {
    const withDomain = store({ domains: [{ host: 'a.test', isPrimary: true }] });
    expect(resumeStep(store(), [])).toBe('domains');
    expect(resumeStep(withDomain, [])).toBe('admin');
    expect(resumeStep(withDomain, [admin({ invitationPending: true })])).toBe('review');
  });

  it('خطوات المعالج بالترتيب', () => {
    expect(nextStep('branding')).toBe('domains');
    expect(nextStep('review')).toBeNull();
    expect(previousStep('branding')).toBeNull();
  });
});

describe('دورة الحياة كما في Tenant', () => {
  it('قيد التجهيز: تفعيل أو أرشفة؛ لا إيقاف لمتجر لم يُفتح', () => {
    expect(lifecycleActions('Provisioning')).toEqual(['Activate', 'Archive']);
  });
  it('الفعّال يُوقَف أو يُؤرشف؛ الموقوف يُعاد تفعيله أو يُؤرشف', () => {
    expect(lifecycleActions('Active')).toEqual(['Suspend', 'Archive']);
    expect(lifecycleActions('Suspended')).toEqual(['Activate', 'Archive']);
  });
  it('المؤرشف نهائي', () => {
    expect(lifecycleActions('Archived')).toEqual([]);
  });
});

describe('دعوة المدير', () => {
  it('الاسم والبريد مطلوبان وبحدود الخادم', () => {
    expect(adminInviteProblems({ fullName: '', email: '' }, options))
      .toEqual({ fullName: { key: 'adminNameRequired' }, email: { key: 'adminEmailRequired' } });
    expect(adminInviteProblems({ fullName: 'a'.repeat(151), email: 'x' }, options))
      .toEqual({ fullName: { key: 'adminNameTooLong', values: { max: 150 } }, email: { key: 'adminEmailInvalid' } });
  });
});

describe('الاستئناف من صفّ القائمة', async () => {
  const { resumeStepFromSummary } = await import('./provisioning');
  it('يستعمل أعداد الخادم لا تقديراً', () => {
    expect(resumeStepFromSummary({ primaryHost: null, activeAdmins: 0, pendingAdminInvitations: 0 })).toBe('domains');
    expect(resumeStepFromSummary({ primaryHost: 'a.test', activeAdmins: 0, pendingAdminInvitations: 0 })).toBe('admin');
    expect(resumeStepFromSummary({ primaryHost: 'a.test', activeAdmins: 0, pendingAdminInvitations: 1 })).toBe('review');
  });
});
