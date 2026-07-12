import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { api } from '../../api/client';
import FormField, { inputClass } from '../../components/common/FormField';
import Button from '../../components/common/Button';
import AuthLayout from './AuthLayout';
import styles from './Auth.module.css';

// صفحة "نسيت كلمة المرور": بريد فقط. الخادم يُرجع نجاحاً دائماً (بلا كشف إن
// كان البريد مسجّلاً) فلا حاجة لتمييز الحالتين هنا — رسالة نجاح موحّدة دوماً.
export default function ForgotPassword() {
  const { t } = useTranslation();
  const [email, setEmail] = useState('');
  const [touched, setTouched] = useState(false);
  const [serverError, setServerError] = useState(null);
  const [busy, setBusy] = useState(false);
  const [sent, setSent] = useState(false);

  const error = !email ? t('auth.emailRequired')
    : !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email) ? t('auth.emailInvalid') : null;

  const submit = async (e) => {
    e.preventDefault();
    setTouched(true);
    if (error) return;
    setBusy(true); setServerError(null);
    try {
      await api.forgotPassword(email.trim());
      setSent(true);
    } catch (err) {
      setServerError(err.message);
    } finally { setBusy(false); }
  };

  return (
    <AuthLayout title={t('auth.forgotPasswordTitle')} subtitle={t('auth.forgotPasswordSubtitle')} serverError={serverError}>
      {sent ? (
        <>
          <p className={styles.successBox}>{t('auth.forgotPasswordSuccess')}</p>
          <p className={styles.switch}><Link to="/login">{t('auth.backToLogin')}</Link></p>
        </>
      ) : (
        <form onSubmit={submit} noValidate>
          <FormField label={t('auth.emailLabel')} error={touched && error}>
            <input type="email" dir="ltr" value={email} autoComplete="email"
              className={inputClass(touched && error)}
              onChange={(e) => setEmail(e.target.value)}
              onBlur={() => setTouched(true)}
              placeholder="you@example.com" />
          </FormField>

          <Button type="submit" variant="saffron" size="lg" loading={busy} className={styles.submit}>
            {t('auth.forgotPasswordSubmit')}
          </Button>

          <p className={styles.switch}><Link to="/login">{t('auth.backToLogin')}</Link></p>
        </form>
      )}
    </AuthLayout>
  );
}
