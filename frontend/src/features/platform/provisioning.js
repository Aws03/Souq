// ============================================================================
// تجهيز متجر من المنصّة — منطق خالص مُختبَر.
//
// الخادم يفرض كل قاعدة (تجمّع Tenant، TenantDomain، دعوة المدير). ما هنا ثلاثة أشياء لا قواعد جديدة:
//   • فحص مسبق بحدود يقرؤها من الخادم (GET /api/platform/tenants/options)، كي تُقال المشكلة بلغة المالك
//     لا برمز InvalidTenantOperation واحد لكل شيء.
//   • جاهزية التسليم: قراءة لحالة المتجر (نطاق؟ مدير؟) — تُعرض ولا تمنع. الخادم لا يشترط نطاقاً ولا مديراً
//     لتفعيل متجر، والواجهة لا تخترع شرطاً لم يُكتب؛ تقول ما ينقص وتترك القرار للمالك.
//   • أين يُستأنف تجهيزٌ توقّف: كل خطوة تُحفظ فوراً، فالمتجر نفسه هو حالة المعالج.
// ============================================================================

export const SETUP_STEPS = ['branding', 'domains', 'modules', 'admin', 'review'];

// صيغة المعرّف كما في Tenant.SlugPattern.
const SLUG = /^[a-z0-9]+(?:-[a-z0-9]+)*$/;
// صيغة المضيف كما في TenantDomain.HostPattern (بعد القصّ والحروف الصغيرة وإسقاط النقطة الأخيرة).
const HOST = /^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?)*$/;
const TIME_ZONE = /^(?:UTC|[A-Za-z_]+(?:\/[A-Za-z0-9_+-]+)+)$/;
const CURRENCY = /^[A-Z]{3}$/;
const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

// اقتراح معرّف من الاسم اللاتيني. اسم عربي لا يُنتج شيئاً — والمالك يكتبه؛ لا تحويل حروف يخترع معرّفاً.
export function slugFromName(name, max = 40) {
  return (name ?? '')
    .toLowerCase()
    .normalize('NFKD').replace(/[̀-ͯ]/g, '')
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, max)
    .replace(/-+$/g, '');
}

export const identityToForm = () => ({ name: '', slug: '', currency: '', defaultCulture: 'ar', timeZone: 'UTC' });

export const buildIdentityPayload = (form) => ({
  name: form.name.trim(),
  slug: form.slug.trim().toLowerCase(),
  currency: form.currency.trim().toUpperCase(),
  defaultCulture: form.defaultCulture,
  timeZone: form.timeZone.trim(),
});

// أوّل مشكلة لكل حقل، بمفتاح ترجمة وقيم.
export function identityProblems(form, options) {
  const { limits } = options;
  const problems = {};
  const name = form.name.trim();
  if (name.length < limits.nameMin || name.length > limits.nameMax) {
    problems.name = { key: 'nameLength', values: { min: limits.nameMin, max: limits.nameMax } };
  } else if (/\p{Cc}/u.test(name)) {
    problems.name = { key: 'nameControl' };
  }

  const slug = form.slug.trim().toLowerCase();
  if (slug.length < limits.slugMin || slug.length > limits.slugMax || !SLUG.test(slug)) {
    problems.slug = { key: 'slugInvalid', values: { min: limits.slugMin, max: limits.slugMax } };
  } else if (options.reservedSlugs.includes(slug)) {
    problems.slug = { key: 'slugReserved', values: { slug } };
  }

  if (!CURRENCY.test(form.currency.trim().toUpperCase())) problems.currency = { key: 'currencyInvalid' };
  if (!options.settings.cultures.includes(form.defaultCulture)) problems.defaultCulture = { key: 'cultureInvalid' };
  const zone = form.timeZone.trim();
  if (!zone || zone.length > limits.timeZoneMax || !TIME_ZONE.test(zone)) problems.timeZone = { key: 'timeZoneInvalid' };
  return problems;
}

export const normalizeHost = (host) => (host ?? '').trim().replace(/\.$/, '').toLowerCase();

export function hostProblem(host, options, existing = []) {
  const normalized = normalizeHost(host);
  if (!normalized) return { key: 'hostRequired' };
  // المنفذ ليس جزءاً من النطاق: "shop.test:5173" خطأ شائع عند النسخ من شريط العنوان.
  if (normalized.includes(':') || normalized.includes('/')) return { key: 'hostNoPort' };
  if (normalized.length > options.limits.hostMax || !HOST.test(normalized)) return { key: 'hostInvalid' };
  if (existing.some((d) => d.host === normalized)) return { key: 'hostDuplicate' };
  return null;
}

export function adminInviteProblems(form, options) {
  const problems = {};
  const name = form.fullName.trim();
  if (!name) problems.fullName = { key: 'adminNameRequired' };
  else if (name.length > options.limits.fullNameMax) problems.fullName = { key: 'adminNameTooLong', values: { max: options.limits.fullNameMax } };
  const email = form.email.trim();
  if (!email) problems.email = { key: 'adminEmailRequired' };
  else if (email.length > options.limits.emailMax || !EMAIL.test(email)) problems.email = { key: 'adminEmailInvalid' };
  return problems;
}

// ── جاهزية التسليم ─────────────────────────────────────────────────────────
// accounts: حسابات إدارة المتجر (GET .../accounts). مدير فعّال = TenantAdmin غير موقوف قَبِل دعوته.
export function adminState(accounts = []) {
  const admins = accounts.filter((a) => a.role === 'TenantAdmin' && a.status === 'Active');
  if (admins.some((a) => !a.invitationPending)) return 'active';
  if (admins.length > 0) return 'invited';
  return 'none';
}

export function readiness(store, accounts) {
  const primary = store.domains.find((d) => d.isPrimary) ?? null;
  const admin = adminState(accounts);
  return {
    items: [
      { id: 'domain', state: primary ? 'done' : 'missing', values: { host: primary?.host ?? '' } },
      { id: 'domainVerified', state: !primary ? 'blocked' : primary.verifiedAt ? 'done' : 'attention' },
      { id: 'admin', state: admin === 'active' ? 'done' : admin === 'invited' ? 'attention' : primary ? 'missing' : 'blocked' },
      { id: 'status', state: store.status === 'Active' ? 'done' : store.status === 'Provisioning' ? 'missing' : 'attention', values: { status: store.status } },
    ],
    primaryHost: primary?.host ?? null,
    admin,
  };
}

// الخطوة التي يُستأنف منها تجهيزٌ توقّف: أوّل ما ينقص فعلاً. الهوية والوحدات لها قيم افتراضية صالحة،
// فلا يُعرف "لم تُضبط بعد" من المتجر — ولا يُفترض.
export function resumeStep(store, accounts) {
  if (!store.domains.some((d) => d.isPrimary)) return 'domains';
  if (adminState(accounts) === 'none') return 'admin';
  return 'review';
}

// ── دورة الحياة ────────────────────────────────────────────────────────────
// كما في Tenant: التفعيل من أيّ حالة غير المؤرشفة، والإيقاف من الفعّال **أو قيد التجهيز** (C3 —
// متجرٌ لم يُفتَح بعد قد يجب إيقافه لسبب تجاري، وقبلها كانت الأرشفة النهائية مخرجه الوحيد)،
// والأرشفة من غير المؤرشف. إخفاء إجراءٍ يرفضه الخادم حتماً يوفّر خطأً لا أكثر؛ الخادم يبقى الحَكَم.
export function lifecycleActions(status) {
  if (status === 'Archived') return [];
  const actions = [];
  if (status !== 'Active') actions.push('Activate');
  if (status !== 'Suspended') actions.push('Suspend');
  actions.push('Archive');
  return actions;
}

// تفعيل متجر ينقصه نطاق أو مدير مسموح على الخادم — لكنه قرارٌ يُقال للمالك قبل أن يتّخذه.
export const activationWarnings = (ready) =>
  ready.items.filter((i) => (i.id === 'domain' || i.id === 'admin') && i.state !== 'done').map((i) => i.id);

export const nextStep = (step) => SETUP_STEPS[SETUP_STEPS.indexOf(step) + 1] ?? null;
export const previousStep = (step) => SETUP_STEPS[SETUP_STEPS.indexOf(step) - 1] ?? null;

// كما في Tenant.RemoveDomain: النطاق الأساسي لا يُحذف ما دام للمتجر غيره — يُعيَّن أساسيٌّ آخر أوّلاً.
export const canRemoveDomain = (domain, domains) => !(domain.isPrimary && domains.length > 1);

// الشيء نفسه من صفّ القائمة (TenantSummaryDto): الخادم يعدّ المديرين هناك بدقّة، فلا حاجة لقراءة الحسابات.
export function resumeStepFromSummary(row) {
  if (!row.primaryHost) return 'domains';
  if (row.activeAdmins + row.pendingAdminInvitations === 0) return 'admin';
  return 'review';
}
