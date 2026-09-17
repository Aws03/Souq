// ============================================================================
// سجلّ التدقيق — منطق خالص مُختبَر: المرشّحات في الرابط، وتحويلها إلى معاملات الخادم، وقراءة السطر.
//
// الخادم وحده يُرشّح ويرقّم (GET /api/platform/audit): متجر، بادئة فعل، حساب فاعل، مدّة، صفحة. لا تصفية في
// المتصفّح فوق صفحة من الخادم — صفحة "مُصفّاة محلياً" تكذب في عدد ما وجدته وفي ما يليها.
//
// المرشّحات في الرابط: تحقيقٌ يُشارَك ويُعاد فتحه ويعمل معه زرّ الرجوع. وتُطبَّق بزرّ لا بكل ضغطة: قراءة السجلّ
// نفسها تُدقَّق (platform.audit.viewed)، فبحثٌ فوري كان سيكتب سطراً لكل حرف.
// ============================================================================

export const AUDIT_PAGE_SIZE = 50;
export const ACTION_MAX = 80;   // AuditEntry.ActionMaxLength ومُحقّق الاستعلام

// فئات الأفعال بادئاتها كما يكتبها الخادم (AuditRecord). الفلتر بادئة، فـ "tenant." يعيد كل أفعال المتاجر.
export const ACTION_GROUPS = [
  { key: 'tenants', prefix: 'tenant.' },
  { key: 'platformAccounts', prefix: 'platform.user.' },
  { key: 'platformReads', prefix: 'platform.' },
  { key: 'storeSettings', prefix: 'store.' },
  { key: 'catalog', prefix: 'catalog.' },
  { key: 'inventory', prefix: 'inventory.' },
  { key: 'orders', prefix: 'order.' },
  { key: 'customers', prefix: 'customer.' },
  { key: 'reviews', prefix: 'review.' },
];

// الأفعال المعروفة وقت كتابة هذه الشاشة، لعنوانٍ مقروء. فعلٌ يضيفه الخادم لاحقاً يُعرض برمزه كما هو — لا يُخفى
// ولا يُخمَّن معناه.
export const KNOWN_ACTIONS = [
  'tenant.created', 'tenant.updated', 'tenant.activated', 'tenant.suspended', 'tenant.archived',
  'tenant.domain.added', 'tenant.domain.removed', 'tenant.domain.primary-set', 'tenant.domain.verified',
  'tenant.settings.updated', 'tenant.branding.uploaded', 'tenant.modules.updated', 'tenant.admin.invited',
  'tenant.payments.viewed', 'tenant.payments.updated', 'tenant.payments.removed',
  'platform.user.invited', 'platform.user.enabled', 'platform.user.disabled',
  'platform.users.listed', 'platform.tenants.listed', 'platform.tenant.viewed', 'platform.tenant.accounts.viewed',
  'platform.provisioning.options.viewed', 'platform.stats.viewed', 'platform.audit.viewed',
  'store.settings.updated', 'store.branding.uploaded', 'store.reviews.updated', 'store.payments.updated',
  'store.payments.removed', 'store.staff.invited', 'store.staff.enabled', 'store.staff.disabled', 'store.dashboard.viewed',
  'catalog.category.created', 'catalog.category.updated', 'catalog.category.deleted',
  'catalog.product.created', 'catalog.product.updated', 'catalog.product.archived', 'catalog.product.status-changed',
  'catalog.product.image-added', 'catalog.product.image-removed', 'catalog.product.images-reordered',
  'catalog.product.video-uploaded',
  'inventory.adjusted', 'inventory.threshold-changed',
  'order.refund.requested', 'order.refund.retried',
  'customer.status-changed', 'customer.data-exported', 'customer.erased',
  'review.approved', 'review.rejected',
];

const KNOWN = new Set(KNOWN_ACTIONS);
const POSITIVE_INT = /^[1-9]\d{0,9}$/;
const DAY = /^\d{4}-\d{2}-\d{2}$/;

export const isKnownAction = (action) => KNOWN.has(action);

// فئة الفعل أطول بادئة تطابقه: "platform.user.invited" لحسابات المنصّة لا لـ "platform." العامّة.
export function groupOf(action) {
  return ACTION_GROUPS.filter((g) => action.startsWith(g.prefix))
    .sort((a, b) => b.prefix.length - a.prefix.length)[0]?.key ?? null;
}

export const actionsInGroup = (key) => KNOWN_ACTIONS.filter((a) => groupOf(a) === key);

// مفتاح ترجمة الفعل: النقاط والشرطات لا تصلح في مسار مفتاح i18next.
export const actionKey = (action) => action.replace(/[.-]/g, '_');

export const emptyFilters = () => ({ tenantId: '', action: '', actorUserId: '', from: '', to: '' });

/** @param {URLSearchParams} search */
export function filtersFromSearch(search) {
  const pick = (name, valid) => {
    const value = (search.get(name) ?? '').trim();
    return valid(value) ? value : '';
  };
  return {
    tenantId: pick('tenantId', (v) => POSITIVE_INT.test(v)),
    action: pick('action', (v) => v.length <= ACTION_MAX),
    actorUserId: pick('actorUserId', (v) => POSITIVE_INT.test(v)),
    from: pick('from', (v) => DAY.test(v)),
    to: pick('to', (v) => DAY.test(v)),
  };
}

export function pageFromSearch(search) {
  const value = search.get('page') ?? '';
  return POSITIVE_INT.test(value) ? Number(value) : 1;
}

// الرابط لا يحمل إلا ما ضُبط: رابط تحقيقٍ قصير ومقروء.
export function searchFromFilters(filters, page = 1) {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(filters)) {
    if (String(value ?? '').trim()) search.set(key, String(value).trim());
  }
  if (page > 1) search.set('page', String(page));
  return search;
}

// مشكلات النموذج قبل الإرسال — الخادم يفحصها أيضاً (بداية بعد نهاية ⇒ 400)، وهذا يوفّر رحلة وسطر تدقيق.
export function filterProblems(filters) {
  const problems = {};
  if (filters.tenantId && !POSITIVE_INT.test(filters.tenantId.trim())) problems.tenantId = 'idInvalid';
  if (filters.actorUserId && !POSITIVE_INT.test(filters.actorUserId.trim())) problems.actorUserId = 'idInvalid';
  if (filters.action.trim().length > ACTION_MAX) problems.action = 'actionTooLong';
  if (filters.from && filters.to && filters.from > filters.to) problems.to = 'rangeReversed';
  return problems;
}

// اليوم يُقرأ بتوقيت من يحقّق: "من 17 أيلول" تعني منتصف ليل يومه هو، لا منتصف ليل UTC. "إلى" تشمل يومها كاملاً.
export function dayStartUtc(day) {
  const [y, m, d] = day.split('-').map(Number);
  return new Date(y, m - 1, d, 0, 0, 0, 0).toISOString();
}

export function dayEndUtc(day) {
  const [y, m, d] = day.split('-').map(Number);
  return new Date(y, m - 1, d, 23, 59, 59, 999).toISOString();
}

export function auditQuery(filters, page) {
  return {
    tenantId: filters.tenantId || undefined,
    action: filters.action || undefined,
    actorUserId: filters.actorUserId || undefined,
    from: filters.from ? dayStartUtc(filters.from) : undefined,
    to: filters.to ? dayEndUtc(filters.to) : undefined,
    page,
    pageSize: AUDIT_PAGE_SIZE,
  };
}

export const hasFilters = (filters) => Object.values(filters).some((v) => String(v ?? '').trim() !== '');

// البيانات الوصفية نصّ JSON يختاره الطلب حقلاً حقلاً. كائنٌ ⇒ أزواج مقروءة؛ غير ذلك (مقصوص عند 4000 حرف،
// أو ليس كائناً) ⇒ يُعرض النصّ كما خُزّن، لا يُسقط ولا يُصلَح.
export function readMetadata(metadata) {
  if (metadata == null || metadata === '') return { entries: [], raw: null };
  try {
    const value = JSON.parse(metadata);
    if (value && typeof value === 'object' && !Array.isArray(value)) {
      return {
        entries: Object.entries(value).map(([key, v]) => [key, v == null ? null : typeof v === 'object' ? JSON.stringify(v) : String(v)]),
        raw: null,
      };
    }
  } catch {
    // يُعرض خاماً أدناه.
  }
  return { entries: [], raw: metadata };
}

// من فعل؟ الخادم يسجّل معرّف الحساب ودوره فقط. لا حساب مسجَّل ⇒ عمل للنظام (منطقة System) أو سطر بلا فاعل
// معروف — ولا يُنسب إلى أحد.
export function actorKind(entry, currentUserId) {
  if (entry.actorUserId == null) return entry.area === 'System' ? 'system' : 'unknown';
  return entry.actorUserId === currentUserId ? 'you' : 'account';
}
