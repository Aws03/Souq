import { describe, it, expect } from 'vitest';
import { accountState, buildInvitePayload, inviteProblems, inviteToForm, staffActions } from './staffView';

// ============================================================================
// فريق المتجر: الحالة كما يفهمها المدير، والإجراءات المعروضة لكل صف. الخادم يحرس القواعد
// (لا إيقاف للنفس، لا إيقاف لآخر مدير)؛ ما يُختبر هنا ألّا تعرض الواجهة إجراءً معروفَ الرفض
// ولا تُخفي إجراءً قد يُقبل.
// ============================================================================
const account = (patch = {}) => ({
  id: 7, fullName: 'Clerk', email: 'clerk@store.test', role: 'TenantStaff', status: 'Active',
  invitationPending: false, emailConfirmed: true, lastLoginAt: null, createdAt: '2026-09-01T00:00:00Z', ...patch,
});

describe('حالة الحساب', () => {
  it('دعوة لم تُقبل ليست حساباً فعّالاً ولو كانت حالته Active', () => {
    expect(accountState(account({ invitationPending: true }))).toBe('invited');
  });

  it('الإيقاف يسبق الدعوة المعلّقة', () => {
    expect(accountState(account({ status: 'Disabled', invitationPending: true }))).toBe('disabled');
  });
});

describe('الإجراءات', () => {
  it('المدير لا يُعرض عليه إيقاف نفسه', () => {
    expect(staffActions(account({ id: 1 }), 1)).toEqual([]);
    expect(staffActions(account({ id: 7 }), 1)).toEqual(['disable']);
  });

  it('دعوة معلّقة تُعاد وتُلغى بالإيقاف', () => {
    expect(staffActions(account({ invitationPending: true }), 1)).toEqual(['resend', 'disable']);
  });

  it('الموقوف يُفعَّل ولا يُعرض عليه إيقاف', () => {
    expect(staffActions(account({ status: 'Disabled' }), 1)).toEqual(['enable']);
  });

  it('آخر مدير لا يُخفى إيقافه: الواجهة لا تعرف عدد المدراء، والخادم يرفض برمزه', () => {
    expect(staffActions(account({ role: 'TenantAdmin' }), 1)).toEqual(['disable']);
  });
});

describe('نموذج الدعوة', () => {
  it('يبدأ موظّفاً لا مديراً: الصلاحية الأوسع اختيار صريح', () => {
    expect(inviteToForm().role).toBe('TenantStaff');
  });

  it('الاسم والبريد مطلوبان، والبريد بشكله', () => {
    expect(inviteProblems(inviteToForm())).toEqual({ fullName: 'nameRequired', email: 'emailRequired' });
    expect(inviteProblems({ fullName: 'A', email: 'nope', role: 'TenantStaff' })).toEqual({ email: 'emailInvalid' });
  });

  it('حدّ الاسم هو حدّ الخادم (150)', () => {
    expect(inviteProblems({ fullName: 'a'.repeat(150), email: 'a@b.co', role: 'TenantStaff' })).toEqual({});
    expect(inviteProblems({ fullName: 'a'.repeat(151), email: 'a@b.co', role: 'TenantStaff' }).fullName).toBe('nameTooLong');
  });

  it('لا دور خارج دورَي المتجر', () => {
    expect(inviteProblems({ fullName: 'A', email: 'a@b.co', role: 'PlatformOwner' }).role).toBe('roleInvalid');
  });

  it('البريد يُرسل مقصوصاً بحروف صغيرة', () => {
    expect(buildInvitePayload({ fullName: ' A ', email: ' Clerk@Store.TEST ', role: 'TenantAdmin' }))
      .toEqual({ fullName: 'A', email: 'clerk@store.test', role: 'TenantAdmin' });
  });
});
