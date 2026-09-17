// ============================================================================
// فريق المتجر وحسابات المنصّة — منطق خالص مُختبَر: حالة الحساب كما تُقرأ، والإجراءات المتاحة لكل صف، وفحص نموذج الدعوة.
//
// الإجراءات هنا عرضٌ لا حماية: الخادم يرفض إيقاف النفس (CannotDisableSelf) وإيقاف آخر مدير فعّال
// (LastAdministrator) مهما عرضت الواجهة. إخفاء "إيقاف" عن صفّ المستخدم نفسه يوفّر عليه خطأً، لا أكثر —
// وآخر مدير لا يُخفى إجراؤه، لأن الواجهة لا تعرف عدد المدراء الفعّالين في كل الصفحات، والخادم يعرف.
// ============================================================================

export const STAFF_ROLES = ['TenantAdmin', 'TenantStaff'];
// أدوار حسابات المنصّة كما يقبلها InvitePlatformUserValidator (Roles.IsPlatform). الافتراضي أقلّها صلاحية.
export const PLATFORM_ROLES = ['PlatformAdmin', 'PlatformOwner'];
export const PAGE_SIZE = 20;

const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
const FULL_NAME_MAX = 150;
const EMAIL_MAX = 256;

// دعوة لم تُقبل بعد تسبق الحالة: الحساب "فعّال" تقنياً لكن لا أحد يستطيع الدخول به.
export function accountState(account) {
  if (account.status === 'Disabled') return 'disabled';
  if (account.invitationPending) return 'invited';
  return 'active';
}

export function staffActions(account, currentUserId) {
  const state = accountState(account);
  const actions = [];
  if (state === 'invited') actions.push('resend');
  if (state === 'disabled') actions.push('enable');
  else if (account.id !== currentUserId) actions.push('disable');
  return actions;
}

export const inviteToForm = (role = 'TenantStaff') => ({ fullName: '', email: '', role });

export const buildInvitePayload = (form) => ({
  fullName: form.fullName.trim(),
  email: form.email.trim().toLowerCase(),
  role: form.role,
});

// أوّل مشكلة لكل حقل، بمفتاح ترجمة. حدود الطول هي حدود User في الخادم (User.FullNameMaxLength/EmailMaxLength).
export function inviteProblems(form, roles = STAFF_ROLES) {
  const problems = {};
  const name = form.fullName.trim();
  if (!name) problems.fullName = 'nameRequired';
  else if (name.length > FULL_NAME_MAX) problems.fullName = 'nameTooLong';
  const email = form.email.trim();
  if (!email) problems.email = 'emailRequired';
  else if (email.length > EMAIL_MAX || !EMAIL.test(email)) problems.email = 'emailInvalid';
  if (!roles.includes(form.role)) problems.role = 'roleInvalid';
  return problems;
}
