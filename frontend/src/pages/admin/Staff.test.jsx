// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { withQueryClient } from '../../test/queryWrapper';

// ============================================================================
// شاشة الفريق. الخادم يحرس القواعد؛ ما يُختبر هنا أن الشاشة:
//   • لا تعرض على المدير إيقاف نفسه،
//   • تسأل قبل الإيقاف بحوار تأكيد ولا تُرسل شيئاً إن تراجع،
//   • تُبلغ رفض الخادم كما هو (آخر مدير فعّال) داخل الحوار لا نجاحاً،
//   • وتفحص الدعوة قبل إرسالها وتُرسل الدور الذي اختير صراحةً.
// ============================================================================
vi.mock('react-i18next', () => ({
  useTranslation: () => ({
    t: (key, values) => (values && Object.keys(values).length ? `${key}:${JSON.stringify(values)}` : key),
    i18n: { dir: () => 'ltr', language: 'en' },
  }),
}));
const toast = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn() }));
vi.mock('../../context/ToastContext', () => ({ useToast: () => toast }));
vi.mock('../../context/AuthContext', () => ({ useAuth: () => ({ user: { id: 1, fullName: 'Owner' } }) }));
vi.mock('../../i18n', () => ({ formatDateTime: (v) => `at ${v}` }));

const client = vi.hoisted(() => ({ getStaff: vi.fn(), inviteStaff: vi.fn(), setStaffStatus: vi.fn() }));
vi.mock('../../api/client', () => ({ api: client }));

const Staff = (await import('./Staff')).default;

const page = (items) => ({ items, page: 1, pageSize: 20, totalCount: items.length, totalPages: 1 });
const member = (patch) => ({
  role: 'TenantStaff', status: 'Active', invitationPending: false, emailConfirmed: true,
  lastLoginAt: null, createdAt: '2026-09-01T00:00:00Z', ...patch,
});
const team = () => page([
  member({ id: 1, fullName: 'Owner', email: 'owner@acme.test', role: 'TenantAdmin', lastLoginAt: '2026-09-16T08:00:00Z' }),
  member({ id: 2, fullName: 'Clerk', email: 'clerk@acme.test' }),
  member({ id: 3, fullName: 'Newcomer', email: 'new@acme.test', invitationPending: true }),
]);

const row = (name) => screen.getByText(name).closest('tr');
const openActions = async (user, name) => {
  await user.click(within(row(name)).getByRole('button'));
};

beforeEach(() => {
  Object.values(client).forEach((fn) => fn.mockReset());
  toast.success.mockReset(); toast.error.mockReset();
  client.getStaff.mockResolvedValue(team());
});

describe('القائمة', () => {
  it('الحالة كما تُقرأ: دعوة معلّقة ليست حساباً فعّالاً، ومن لم يدخل "لم يدخل"', async () => {
    render(withQueryClient(<Staff />));
    await screen.findByText('Clerk');

    expect(within(row('Newcomer')).getByText('admin.staff.state.invited')).toBeInTheDocument();
    expect(within(row('Clerk')).getByText('admin.staff.never')).toBeInTheDocument();
    expect(within(row('Owner')).getByText('at 2026-09-16T08:00:00Z')).toBeInTheDocument();
    expect(client.getStaff).toHaveBeenCalledWith({ page: 1, pageSize: 20 });
  });

  it('صفّ المدير نفسه بلا إجراءات: لا يُعرض عليه إيقاف نفسه', async () => {
    render(withQueryClient(<Staff />));
    await screen.findByText('Clerk');
    expect(within(row('Owner')).queryByRole('button')).toBeNull();
    expect(within(row('Owner')).getByText(/admin\.staff\.you/)).toBeInTheDocument();
  });
});

describe('الإيقاف', () => {
  it('يسأل أولاً بحوار تأكيد، ولا يُرسل شيئاً إن تراجع المدير', async () => {
    render(withQueryClient(<Staff />));
    await screen.findByText('Clerk');
    const user = userEvent.setup();

    await openActions(user, 'Clerk');
    await user.click(await screen.findByRole('menuitem', { name: 'admin.staff.action.disable' }));

    const dialog = await screen.findByRole('alertdialog', { name: /admin\.staff\.confirmDisable\.title/ });
    expect(dialog).toHaveAccessibleDescription('admin.staff.confirmDisable.message');
    expect(client.setStaffStatus).not.toHaveBeenCalled();

    await user.keyboard('{Escape}');
    await waitFor(() => expect(screen.queryByRole('alertdialog')).toBeNull());
    expect(client.setStaffStatus).not.toHaveBeenCalled();
  });

  it('التأكيد يوقف الحساب ويُغلق الحوار', async () => {
    client.setStaffStatus.mockResolvedValue(undefined);
    render(withQueryClient(<Staff />));
    await screen.findByText('Clerk');
    const user = userEvent.setup();

    await openActions(user, 'Clerk');
    await user.click(await screen.findByRole('menuitem', { name: 'admin.staff.action.disable' }));
    const dialog = await screen.findByRole('alertdialog');
    await user.click(within(dialog).getByRole('button', { name: 'admin.staff.confirmDisable.action' }));

    await waitFor(() => expect(screen.queryByRole('alertdialog')).toBeNull());
    expect(client.setStaffStatus).toHaveBeenCalledWith(2, false);
    expect(toast.success).toHaveBeenCalled();
  });

  it('رفض الخادم (آخر مدير فعّال) يُقرأ داخل الحوار لا نجاحاً', async () => {
    client.setStaffStatus.mockRejectedValue(new Error('The last active administrator can\'t be disabled.'));
    render(withQueryClient(<Staff />));
    await screen.findByText('Clerk');
    const user = userEvent.setup();

    await openActions(user, 'Clerk');
    await user.click(await screen.findByRole('menuitem', { name: 'admin.staff.action.disable' }));
    const dialog = await screen.findByRole('alertdialog');
    await user.click(within(dialog).getByRole('button', { name: 'admin.staff.confirmDisable.action' }));

    expect(await within(dialog).findByRole('alert')).toHaveTextContent('The last active administrator can\'t be disabled.');
    expect(client.setStaffStatus).toHaveBeenCalledWith(2, false);
    expect(toast.success).not.toHaveBeenCalled();
  });

  it('إعادة الدعوة تُرسل الاسم والبريد والدور نفسها', async () => {
    client.inviteStaff.mockResolvedValue({ userId: 3, renewed: true });
    render(withQueryClient(<Staff />));
    await screen.findByText('Clerk');
    const user = userEvent.setup();

    await openActions(user, 'Newcomer');
    await user.click(await screen.findByRole('menuitem', { name: 'admin.staff.action.resend' }));

    await waitFor(() => expect(client.inviteStaff)
      .toHaveBeenCalledWith({ fullName: 'Newcomer', email: 'new@acme.test', role: 'TenantStaff' }));
  });
});

describe('الدعوة', () => {
  it('لا تُرسل ناقصة، وتُرسل الدور المختار صراحةً', async () => {
    client.inviteStaff.mockResolvedValue({ userId: 9, renewed: false });
    render(withQueryClient(<Staff />));
    await screen.findByText('Clerk');
    const user = userEvent.setup();

    await user.click(screen.getByRole('button', { name: 'admin.staff.invite' }));
    const dialog = await screen.findByRole('dialog');
    await user.click(within(dialog).getByRole('button', { name: 'admin.staff.sendInvite' }));
    expect(client.inviteStaff).not.toHaveBeenCalled();
    expect(within(dialog).getByText('admin.staff.problem.nameRequired')).toBeInTheDocument();

    await user.type(within(dialog).getByLabelText('admin.staff.nameLabel'), 'Manager Two');
    await user.type(within(dialog).getByLabelText('admin.staff.emailLabel'), ' Two@Acme.test ');
    await user.click(within(dialog).getByRole('radio', { name: /admin\.staff\.role\.TenantAdmin/ }));
    await user.click(within(dialog).getByRole('button', { name: 'admin.staff.sendInvite' }));

    await waitFor(() => expect(client.inviteStaff)
      .toHaveBeenCalledWith({ fullName: 'Manager Two', email: 'two@acme.test', role: 'TenantAdmin' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
  });

  it('بريد مستخدم يُبقي الدرج مفتوحاً برسالة الخادم', async () => {
    client.inviteStaff.mockRejectedValue(new Error('This email is already registered.'));
    render(withQueryClient(<Staff />));
    await screen.findByText('Clerk');
    const user = userEvent.setup();

    await user.click(screen.getByRole('button', { name: 'admin.staff.invite' }));
    const dialog = await screen.findByRole('dialog');
    await user.type(within(dialog).getByLabelText('admin.staff.nameLabel'), 'Customer');
    await user.type(within(dialog).getByLabelText('admin.staff.emailLabel'), 'buyer@acme.test');
    await user.click(within(dialog).getByRole('button', { name: 'admin.staff.sendInvite' }));

    expect(await within(dialog).findByText('This email is already registered.')).toBeInTheDocument();
  });
});
