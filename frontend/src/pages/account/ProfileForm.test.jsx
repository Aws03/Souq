// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

// ============================================================================
// نموذج البيانات الشخصية (TD-32).
//
// ما يُختبر هنا هو ما لا يُقاس إلا بالشاشة:
//   • البريد **بريد الدخول**: يُعرض ولا يُعدَّل من هنا، وتغييره لو أمكن يغيّر هويّة الحساب نفسه،
//   • اسمٌ فارغ لا يُرسَل — وحساب بلا اسم يظهر فارغاً في كل طلب وكل رسالة بعد ذلك،
//   • ما يصل إلى الخادم مقصوص، وهاتفٌ فارغ يصل `null` لا `""` (فرقٌ يقرأه الخادم حذفاً لا إبقاءً),
//   • ومساحات وحدها ليست اسماً.
// ============================================================================
vi.mock('react-i18next', () => ({
  useTranslation: () => ({
    t: (key) => key,
    i18n: { dir: () => 'ltr', language: 'en', exists: () => true },
  }),
}));

const ProfileForm = (await import('./ProfileForm')).default;

const PROFILE = { fullName: 'Aws Alfaris', email: 'owner@souq.test', phone: '0790000000' };
const submit = (user) => user.click(screen.getByRole('button', { name: 'account.saveProfile' }));

let onSave;
beforeEach(() => { onSave = vi.fn().mockResolvedValue(undefined); });

describe('ProfileForm', () => {
  it('البريد يُعرض للقراءة فقط — هو بريد الدخول', () => {
    render(<ProfileForm profile={PROFILE} onSave={onSave} />);

    const email = screen.getByLabelText('account.emailLabel');
    expect(email).toHaveValue('owner@souq.test');
    expect(email).toHaveAttribute('readonly');
  });

  it('اسم فارغ لا يُرسَل، والخطأ يُعلَّق على حقله', async () => {
    const user = userEvent.setup();
    render(<ProfileForm profile={{ ...PROFILE, fullName: '' }} onSave={onSave} />);

    await submit(user);

    expect(onSave).not.toHaveBeenCalled();
    expect(screen.getByText('account.nameRequired')).toBeInTheDocument();
  });

  it('مساحات وحدها ليست اسماً', async () => {
    const user = userEvent.setup();
    render(<ProfileForm profile={{ ...PROFILE, fullName: '' }} onSave={onSave} />);

    await user.type(screen.getByLabelText('account.fullNameLabel'), '   ');
    await submit(user);

    expect(onSave).not.toHaveBeenCalled();
  });

  it('يُرسل مقصوصاً، وهاتفاً فارغاً يُرسل null', async () => {
    const user = userEvent.setup();
    render(<ProfileForm profile={{ ...PROFILE, fullName: 'Aws', phone: '' }} onSave={onSave} />);

    const name = screen.getByLabelText('account.fullNameLabel');
    await user.clear(name);
    await user.type(name, '  Aws Alfaris  ');
    await submit(user);

    await waitFor(() => expect(onSave).toHaveBeenCalledTimes(1));
    expect(onSave).toHaveBeenCalledWith({ fullName: 'Aws Alfaris', phone: null });
  });

  it('يبدأ بقيم الحساب، وهاتفٌ غير مضبوط يبدأ فارغاً لا "null"', () => {
    render(<ProfileForm profile={{ ...PROFILE, phone: null }} onSave={onSave} />);

    expect(screen.getByLabelText('account.fullNameLabel')).toHaveValue('Aws Alfaris');
    expect(screen.getByLabelText('account.phoneLabel')).toHaveValue('');
  });
});
