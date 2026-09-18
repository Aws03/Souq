import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../../context/AuthContext';
import { useToast } from '../../context/ToastContext';
import { usePageMetadata } from '../../app/usePageMetadata';
import FormField, { inputClass } from '../../components/common/FormField';
import PasswordInput from '../../components/common/PasswordInput';
import Button from '../../components/common/Button';
import { ErrorBanner } from '../../components/common/StateViews';
import { changePasswordProblems, isChangePasswordValid } from '../../features/account/passwordForm';
import styles from './Account.module.css';

// ============================================================================
// تغيير كلمة المرور (TD-29، بُنيت في M9). كان كل ما تحتها موجوداً — النقطة والمدقّق و`api.changePassword`
// و`AuthContext.changePassword` — ولا شاشة تستدعيها: عميل مسجَّل لم يكن يستطيع تغيير كلمة مروره إلا
// بالخروج وطلب رابط "نسيت كلمة المرور" على بريده.
//
// **صفحة لا لوحة في /account.** الأخيرة كانت تحمل ثلاثة اهتمامات فنُقلت العناوين إلى صفحتها في
// المرحلة 16؛ فإضافة اهتمام رابع تسير عكس قرارٍ متعمّد حديث. القشرة نفسها (AccountLayout) والتنقّل يحملها.
//
// **ما تقوله الشاشة صحيح.** الخادم يُبطل كل الجلسات (`RevokeAllAsync("PasswordChanged")`) ثم يُصدر
// جلسة جديدة لهذا الجهاز، و`api.changePassword` تبدأ بها الجلسة محلياً — فالتنبيه "تُخرجك من كل
// الأجهزة الأخرى وهذا الجهاز يبقى داخلاً" وصفٌ لما يحدث فعلاً، لا وعدٌ متجمّل.
//
// وخطأ "كلمة المرور الحالية غير صحيحة" يُعلَّق على حقله لا في شريط عام: الخادم يعيده رمزاً ثابتاً
// (`CurrentPasswordIncorrect`) بـ 400 — وليس 401 عمداً، لأن الواجهة تعامل 401 كانتهاء جلسة فتُخرج
// المستخدم. إدخالٌ خاطئ لا يجوز أن يُطرد صاحبه.
// ============================================================================
export default function ChangePassword() {
  const { t } = useTranslation();
  const toast = useToast();
  const { changePassword } = useAuth();
  usePageMetadata({ title: t('account.passwordTitle') });

  const [form, setForm] = useState({ currentPassword: '', newPassword: '', confirm: '' });
  const [touched, setTouched] = useState({});
  const [serverError, setServerError] = useState(null);
  const [currentPasswordRejected, setCurrentPasswordRejected] = useState(false);
  const [busy, setBusy] = useState(false);

  const problems = changePasswordProblems(form);
  const field = (name) => (name === 'currentPassword' && currentPasswordRejected
    ? t('errors.codes.CurrentPasswordIncorrect')
    : touched[name] && problems[name] ? t(problems[name]) : null);

  const set = (name) => (e) => {
    setForm((f) => ({ ...f, [name]: e.target.value }));
    if (name === 'currentPassword') setCurrentPasswordRejected(false);
  };
  const blur = (name) => () => setTouched((prev) => ({ ...prev, [name]: true }));

  const submit = async (e) => {
    e.preventDefault();
    setTouched({ currentPassword: true, newPassword: true, confirm: true });
    if (!isChangePasswordValid(problems)) return;

    setBusy(true); setServerError(null); setCurrentPasswordRejected(false);
    try {
      await changePassword(form.currentPassword, form.newPassword);
      // لا تبقى كلمة مرور في حالة الشاشة بعد نجاح العملية.
      setForm({ currentPassword: '', newPassword: '', confirm: '' });
      setTouched({});
      toast.success(t('account.passwordChanged'));
    } catch (err) {
      if (err.code === 'CurrentPasswordIncorrect') setCurrentPasswordRejected(true);
      else setServerError(err.message);
    } finally { setBusy(false); }
  };

  return (
    <section className={styles.panel}>
      <div className={styles.panelHead}>
        <h2 className={styles.panelTitle}>{t('account.passwordTitle')}</h2>
      </div>
      <p className={styles.muted}>{t('account.passwordSubtitle')}</p>

      <form onSubmit={submit} noValidate>
        {serverError && <ErrorBanner message={serverError} />}

        <FormField label={t('account.currentPasswordLabel')} htmlFor="current-password"
          error={field('currentPassword')}>
          <PasswordInput id="current-password" value={form.currentPassword} autoComplete="current-password"
            className={inputClass(!!field('currentPassword'))}
            onChange={set('currentPassword')} onBlur={blur('currentPassword')} />
        </FormField>

        <FormField label={t('auth.newPasswordLabel')} htmlFor="new-password" error={field('newPassword')}>
          <PasswordInput id="new-password" value={form.newPassword} autoComplete="new-password"
            className={inputClass(!!field('newPassword'))} placeholder={t('auth.passwordPlaceholderMin')}
            onChange={set('newPassword')} onBlur={blur('newPassword')} />
        </FormField>

        <FormField label={t('auth.confirmPasswordLabel')} htmlFor="confirm-password" error={field('confirm')}>
          <PasswordInput id="confirm-password" value={form.confirm} autoComplete="new-password"
            className={inputClass(!!field('confirm'))} placeholder={t('auth.confirmPasswordPlaceholder')}
            onChange={set('confirm')} onBlur={blur('confirm')} />
        </FormField>

        {/* التنبيه قبل الزرّ لا بعده: أثرٌ على أجهزة أخرى يُقرأ قبل الالتزام به. */}
        <p className={styles.muted}>{t('account.passwordSignsOutOthers')}</p>
        <Button type="submit" variant="accent" loading={busy}>{t('account.passwordSubmit')}</Button>
      </form>
    </section>
  );
}
