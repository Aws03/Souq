import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../../context/AuthContext';
import FormField, { inputClass } from '../../components/common/FormField';
import PasswordInput from '../../components/common/PasswordInput';
import Button from '../../components/common/Button';
import AuthLayout from './AuthLayout';
import styles from './Auth.module.css';

// صفحة إنشاء حساب. نفس منهج التحقّق الفوري. التسجيل ينشئ عميلاً دائماً
// (لا يُنشأ أدمن من هنا) ويسجّل الدخول تلقائياً ثم يوجّه للمتجر.
export default function Register() {
  const { t } = useTranslation();
  const { register } = useAuth();
  const navigate = useNavigate();

  const [form, setForm] = useState({ fullName: '', email: '', password: '', confirm: '' });
  const [touched, setTouched] = useState({});
  const [serverError, setServerError] = useState(null);
  const [busy, setBusy] = useState(false);

  const set = (k) => (e) => setForm((f) => ({ ...f, [k]: e.target.value }));
  const blur = (k) => () => setTouched((t) => ({ ...t, [k]: true }));

  const errors = {
    fullName: !form.fullName.trim() ? t('auth.fullNameRequired')
      : form.fullName.trim().length < 3 ? t('auth.fullNameTooShort') : null,
    email: !form.email ? t('auth.emailRequired')
      : !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(form.email) ? t('auth.emailInvalid') : null,
    password: !form.password ? t('auth.passwordRequired')
      : form.password.length < 8 ? t('auth.passwordMinLength') : null,
    confirm: form.confirm !== form.password ? t('auth.confirmPasswordMismatch') : null,
  };
  const isValid = !Object.values(errors).some(Boolean);

  const submit = async (e) => {
    e.preventDefault();
    setTouched({ fullName: true, email: true, password: true, confirm: true });
    if (!isValid) return;
    setBusy(true); setServerError(null);
    try {
      await register(form.fullName.trim(), form.email.trim(), form.password);
      navigate('/', { replace: true });
    } catch (err) {
      setServerError(err.message || t('auth.registerFailed'));
    } finally { setBusy(false); }
  };

  const err = (k) => (touched[k] ? errors[k] : null);

  return (
    <AuthLayout title={t('auth.registerTitle')} subtitle={t('auth.registerSubtitle')} serverError={serverError}>
      <form onSubmit={submit} noValidate>
        <FormField label={t('auth.fullNameLabel')} error={err('fullName')}>
          <input value={form.fullName} autoComplete="name" className={inputClass(err('fullName'))}
            onChange={set('fullName')} onBlur={blur('fullName')} placeholder={t('auth.fullNamePlaceholder')} />
        </FormField>

        <FormField label={t('auth.emailLabel')} error={err('email')}>
          <input type="email" dir="ltr" value={form.email} autoComplete="email" className={inputClass(err('email'))}
            onChange={set('email')} onBlur={blur('email')} placeholder="you@example.com" />
        </FormField>

        <FormField label={t('auth.passwordLabel')} error={err('password')}>
          <PasswordInput value={form.password} autoComplete="new-password" className={inputClass(err('password'))}
            onChange={set('password')} onBlur={blur('password')} placeholder={t('auth.passwordPlaceholderMin')} />
        </FormField>

        <FormField label={t('auth.confirmPasswordLabel')} error={err('confirm')}>
          <PasswordInput value={form.confirm} autoComplete="new-password" className={inputClass(err('confirm'))}
            onChange={set('confirm')} onBlur={blur('confirm')} placeholder={t('auth.confirmPasswordPlaceholder')} />
        </FormField>

        <Button type="submit" variant="saffron" size="lg" loading={busy} className={styles.submit}>{t('auth.registerSubmit')}</Button>

        <p className={styles.switch}>{t('auth.haveAccount')} <Link to="/login">{t('auth.signIn')}</Link></p>
      </form>
    </AuthLayout>
  );
}
