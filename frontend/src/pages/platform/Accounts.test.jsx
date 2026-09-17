// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { withQueryClient } from '../../test/queryWrapper';

// ============================================================================
// حسابات المنصّة. الخادم يحرس القواعد (المالك وحده، لا إيقاف للنفس، لا إيقاف لآخر مالك)؛ ما يُختبر هنا:
//   • الدعوة بأدوار المنصّة وحدها، والافتراضي أقلّها صلاحية،
//   • الإيقاف بحوار تأكيد، ورفض الخادم يُقرأ داخله،
//   • لا إجراءات على صفّ المستخدم نفسه، ولا تغيير دور ولا حذف (الخادم لا يقدّمهما).
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

const client = vi.hoisted(() => ({ getPlatformUsers: vi.fn(), invitePlatformUser: vi.fn(), setPlatformUserStatus: vi.fn() }));
vi.mock('../../api/client', () => ({ api: client }));

const Accounts = (await import('./Accounts')).default;

const account = (patch) => ({
  role: 'PlatformAdmin', status: 'Active', invitationPending: false, emailConfirmed: true,
  lastLoginAt: null, createdAt: '2026-09-01T00:00:00Z', ...patch,
});
const accounts = () => ({
  items: [
    account({ id: 1, fullName: 'Owner', email: 'owner@souq.test', role: 'PlatformOwner' }),
    account({ id: 2, fullName: 'Ops', email: 'ops@souq.test' }),
    account({ id: 3, fullName: 'Pending', email: 'pending@souq.test', invitationPending: true }),
    account({ id: 4, fullName: 'Former', email: 'former@souq.test', status: 'Disabled' }),
  ],
  page: 1, pageSize: 20, totalCount: 4, totalPages: 1,
});

const row = (name) => screen.getByText(name).closest('tr');
const menu = async (user, name) => {
  await user.click(within(row(name)).getByRole('button'));
  return screen.findAllByRole('menuitem');
};

beforeEach(() => {
  Object.values(client).forEach((fn) => fn.mockReset());
  toast.success.mockReset(); toast.error.mockReset();
  client.getPlatformUsers.mockResolvedValue(accounts());
});

describe('القائمة', () => {
  it('الأدوار والحالات كما تُقرأ، وصفّ المستخدم نفسه بلا إجراءات', async () => {
    render(withQueryClient(<Accounts />));
    await screen.findByText('Ops');

    expect(client.getPlatformUsers).toHaveBeenCalledWith({ page: 1, pageSize: 20 });
    expect(within(row('Owner')).getByText('platform.accounts.role.PlatformOwner')).toBeInTheDocument();
    expect(within(row('Pending')).getByText('admin.staff.state.invited')).toBeInTheDocument();
    expect(within(row('Former')).getByText('admin.staff.state.disabled')).toBeInTheDocument();
    expect(within(row('Owner')).queryByRole('button')).toBeNull();
  });

  it('إجراءات الصفّ ما يقدّمه الخادم فقط', async () => {
    render(withQueryClient(<Accounts />));
    await screen.findByText('Ops');
    const user = userEvent.setup();

    expect((await menu(user, 'Pending')).map((i) => i.textContent)).toEqual(['admin.staff.action.resend', 'admin.staff.action.disable']);
    await user.keyboard('{Escape}');
    expect((await menu(user, 'Former')).map((i) => i.textContent)).toEqual(['admin.staff.action.enable']);
  });
});

describe('الإيقاف', () => {
  it('لا طلب قبل التأكيد، والتأكيد يوقف الحساب', async () => {
    client.setPlatformUserStatus.mockResolvedValue(undefined);
    render(withQueryClient(<Accounts />));
    await screen.findByText('Ops');
    const user = userEvent.setup();

    await user.click((await menu(user, 'Ops')).find((i) => i.textContent === 'admin.staff.action.disable'));
    const dialog = await screen.findByRole('alertdialog', { name: /platform\.accounts\.confirmDisable\.title/ });
    expect(client.setPlatformUserStatus).not.toHaveBeenCalled();

    await user.click(within(dialog).getByRole('button', { name: 'platform.accounts.confirmDisable.action' }));
    await waitFor(() => expect(screen.queryByRole('alertdialog')).toBeNull());
    expect(client.setPlatformUserStatus).toHaveBeenCalledWith(2, false);
  });

  it('آخر مالك فعّال: رفض الخادم يُقرأ في الحوار ولا نجاح', async () => {
    client.setPlatformUserStatus.mockRejectedValue(new Error('The last active account with this role can\'t be disabled.'));
    render(withQueryClient(<Accounts />));
    await screen.findByText('Ops');
    const user = userEvent.setup();

    await user.click((await menu(user, 'Ops')).find((i) => i.textContent === 'admin.staff.action.disable'));
    const dialog = await screen.findByRole('alertdialog');
    await user.click(within(dialog).getByRole('button', { name: 'platform.accounts.confirmDisable.action' }));

    expect(await within(dialog).findByRole('alert')).toHaveTextContent('can\'t be disabled');
    expect(toast.success).not.toHaveBeenCalled();
  });

  it('التفعيل وإعادة الدعوة بلا حوار، وإعادة الدعوة بالدور نفسه', async () => {
    client.setPlatformUserStatus.mockResolvedValue(undefined);
    client.invitePlatformUser.mockResolvedValue({ userId: 3, renewed: true });
    render(withQueryClient(<Accounts />));
    await screen.findByText('Ops');
    const user = userEvent.setup();

    await user.click((await menu(user, 'Former'))[0]);
    await waitFor(() => expect(client.setPlatformUserStatus).toHaveBeenCalledWith(4, true));
    await user.click((await menu(user, 'Pending'))[0]);
    await waitFor(() => expect(client.invitePlatformUser)
      .toHaveBeenCalledWith({ fullName: 'Pending', email: 'pending@souq.test', role: 'PlatformAdmin' }));
    expect(screen.queryByRole('alertdialog')).toBeNull();
  });
});

describe('الدعوة', () => {
  it('أدوار المنصّة وحدها، والمشرف افتراضياً، والمالك اختيار صريح', async () => {
    client.invitePlatformUser.mockResolvedValue({ userId: 9, renewed: false });
    render(withQueryClient(<Accounts />));
    await screen.findByText('Ops');
    const user = userEvent.setup();

    await user.click(screen.getByRole('button', { name: 'platform.accounts.invite' }));
    const dialog = await screen.findByRole('dialog');
    const radios = within(dialog).getAllByRole('radio');
    expect(radios.map((r) => r.value)).toEqual(['PlatformAdmin', 'PlatformOwner']);
    expect(within(dialog).getByRole('radio', { name: /PlatformAdmin/ })).toBeChecked();

    await user.type(within(dialog).getByLabelText('admin.staff.nameLabel'), 'Second Owner');
    await user.type(within(dialog).getByLabelText('admin.staff.emailLabel'), 'Two@Souq.test');
    await user.click(within(dialog).getByRole('radio', { name: /PlatformOwner/ }));
    await user.click(within(dialog).getByRole('button', { name: 'platform.accounts.sendInvite' }));

    await waitFor(() => expect(client.invitePlatformUser)
      .toHaveBeenCalledWith({ fullName: 'Second Owner', email: 'two@souq.test', role: 'PlatformOwner' }));
  });
});
