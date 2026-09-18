// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

// ============================================================================
// شاشة تغيير كلمة المرور (TD-29). القواعد نفسها مُختبرة نقيّةً في passwordForm.test.js، وعلى الخادم
// في PasswordRulesTests — فما يُختبر هنا هو ما لا يُقاس إلا بالشاشة:
//   • لا تُرسل نموذجاً ترفضه القواعد (رحلةٌ موفَّرة، ورسالة تُقرأ فوراً)،
//   • "الحالية غير صحيحة" تُعلَّق على **حقلها** لا في شريط عامّ، ولا تُخرج المستخدم،
//   • النجاح يُفرِّغ الحقول — لا تبقى كلمة مرور في حالة الشاشة بعد إتمام العملية،
//   • والتنبيه بأن الأجهزة الأخرى ستخرج يُقرأ **قبل** الزرّ لا بعده.
// ============================================================================
vi.mock('react-i18next', () => ({
  useTranslation: () => ({
    t: (key, values) => (values && Object.keys(values).length ? `${key}:${JSON.stringify(values)}` : key),
    i18n: { dir: () => 'ltr', language: 'en', exists: () => true },
  }),
}));
const toast = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn() }));
vi.mock('../../context/ToastContext', () => ({ useToast: () => toast }));
vi.mock('../../app/usePageMetadata', () => ({ usePageMetadata: () => {} }));

const auth = vi.hoisted(() => ({ changePassword: vi.fn() }));
vi.mock('../../context/AuthContext', () => ({ useAuth: () => auth }));

const ChangePassword = (await import('./ChangePassword')).default;

const fill = async (user, values) => {
  if (values.current !== undefined) await user.type(screen.getByLabelText('account.currentPasswordLabel'), values.current);
  if (values.next !== undefined) await user.type(screen.getByLabelText('auth.newPasswordLabel'), values.next);
  if (values.confirm !== undefined) await user.type(screen.getByLabelText('auth.confirmPasswordLabel'), values.confirm);
};
const submit = (user) => user.click(screen.getByRole('button', { name: 'account.passwordSubmit' }));

beforeEach(() => {
  auth.changePassword.mockReset().mockResolvedValue(undefined);
  toast.success.mockReset(); toast.error.mockReset();
});

describe('قبل الإرسال', () => {
  it('نموذج فارغ لا يُرسل، وكل حقل يقول مشكلته', async () => {
    render(<ChangePassword />);
    const user = userEvent.setup();

    await submit(user);

    expect(auth.changePassword).not.toHaveBeenCalled();
    expect(screen.getByText('account.currentPasswordRequired')).toBeInTheDocument();
    expect(screen.getByText('auth.passwordRequired')).toBeInTheDocument();
  });

  it('كلمة بلا رقم لا تُرسل — القاعدة نفسها التي يفرضها الخادم', async () => {
    render(<ChangePassword />);
    const user = userEvent.setup();

    await fill(user, { current: 'old-passw0rd', next: 'aaaaaaaa', confirm: 'aaaaaaaa' });
    await submit(user);

    expect(auth.changePassword).not.toHaveBeenCalled();
    expect(screen.getByText('auth.passwordLettersAndDigits')).toBeInTheDocument();
  });

  it('تأكيد لا يطابق لا يُرسل', async () => {
    render(<ChangePassword />);
    const user = userEvent.setup();

    await fill(user, { current: 'old-passw0rd', next: 'new-passw0rd', confirm: 'new-passw0rdX' });
    await submit(user);

    expect(auth.changePassword).not.toHaveBeenCalled();
    expect(screen.getByText('auth.confirmPasswordMismatch')).toBeInTheDocument();
  });

  it('الأثر على الأجهزة الأخرى مكتوب قبل الزرّ', async () => {
    render(<ChangePassword />);
    const note = screen.getByText('account.passwordSignsOutOthers');
    const button = screen.getByRole('button', { name: 'account.passwordSubmit' });
    // DOCUMENT_POSITION_FOLLOWING: الزرّ يأتي بعد التنبيه في ترتيب المستند.
    expect(note.compareDocumentPosition(button) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });
});

describe('بعد الإرسال', () => {
  it('نموذج صحيح يُرسل الحالية والجديدة، ثم يُفرِّغ الحقول', async () => {
    render(<ChangePassword />);
    const user = userEvent.setup();

    await fill(user, { current: 'old-passw0rd', next: 'new-passw0rd', confirm: 'new-passw0rd' });
    await submit(user);

    await waitFor(() => expect(auth.changePassword).toHaveBeenCalledWith('old-passw0rd', 'new-passw0rd'));
    expect(toast.success).toHaveBeenCalledWith('account.passwordChanged');
    // لا كلمة مرور باقية في الشاشة بعد النجاح.
    await waitFor(() => expect(screen.getByLabelText('account.currentPasswordLabel')).toHaveValue(''));
    expect(screen.getByLabelText('auth.newPasswordLabel')).toHaveValue('');
    expect(screen.getByLabelText('auth.confirmPasswordLabel')).toHaveValue('');
  });

  it('"الحالية غير صحيحة" تُعلَّق على حقلها، ولا تُفرَّغ الحقول', async () => {
    // الخادم يعيدها 400 برمز ثابت لا 401 — لأن 401 تعني للواجهة "انتهت الجلسة" فتُخرج المستخدم.
    // إدخالٌ خاطئ لا يجوز أن يُطرد صاحبه، وهذا الاختبار هو ما يُثبّت ذلك في الشاشة.
    auth.changePassword.mockRejectedValue(Object.assign(new Error('كلمة المرور الحالية غير صحيحة'), {
      status: 400, code: 'CurrentPasswordIncorrect',
    }));
    render(<ChangePassword />);
    const user = userEvent.setup();

    await fill(user, { current: 'wrong-passw0rd', next: 'new-passw0rd', confirm: 'new-passw0rd' });
    await submit(user);

    expect(await screen.findByText('errors.codes.CurrentPasswordIncorrect')).toBeInTheDocument();
    expect(toast.success).not.toHaveBeenCalled();
    // الجديدة والتأكيد يبقيان: لا يُعاد كتابتهما بسبب خطأ في حقل آخر.
    expect(screen.getByLabelText('auth.newPasswordLabel')).toHaveValue('new-passw0rd');

    // والكتابة في الحقل تُزيل الرفض فوراً بدل أن يبقى معلَّقاً على قيمة جديدة.
    await user.type(screen.getByLabelText('account.currentPasswordLabel'), 'X');
    await waitFor(() => expect(screen.queryByText('errors.codes.CurrentPasswordIncorrect')).toBeNull());
  });

  it('خطأ آخر من الخادم يُقرأ في شريط لا على حقل', async () => {
    auth.changePassword.mockRejectedValue(Object.assign(new Error('تعذّر الاتصال'), { status: 503, code: null }));
    render(<ChangePassword />);
    const user = userEvent.setup();

    await fill(user, { current: 'old-passw0rd', next: 'new-passw0rd', confirm: 'new-passw0rd' });
    await submit(user);

    expect(await screen.findByRole('alert')).toHaveTextContent('تعذّر الاتصال');
  });
});
