import { useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { api } from '../../api/client';
import FormField, { inputClass } from '../../components/common/FormField';
import PasswordInput from '../../components/common/PasswordInput';
import Button from '../../components/common/Button';
import AuthLayout from './AuthLayout';
import styles from './Auth.module.css';

// صفحة إعادة تعيين كلمة المرور — تُفتح من رابط البريد (?token=...). بلا رمز
// في الرابط أصلاً (وصول مباشر) نعرض دعوة لطلب رابط جديد بدل نموذج لا معنى له.
export default function ResetPassword() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const token = searchParams.get('token');

  const [password, setPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const [touched, setTouched] = useState({});
  const [serverError, setServerError] = useState(null);
  const [busy, setBusy] = useState(false);
  const [done, setDone] = useState(false);

  const errors = {
    password: !password ? t('auth.passwordRequired')
      : password.length < 8 ? t('auth.passwordMinLength') : null,
    confirm: confirm !== password ? t('auth.confirmPasswordMismatch') : null,
  };
  const isValid = !errors.password && !errors.confirm;

  const submit = async (e) => {
    e.preventDefault();
    setTouched({ password: true, confirm: true });
    if (!isValid) return;
    setBusy(true); setServerError(null);
    try {
      await api.resetPassword(token, password);
      setDone(true);
      setTimeout(() => navigate('/login', { replace: true }), 2000);
    } catch (err) {
      setServerError(err.message);
    } finally { setBusy(false); }
  };

  if (!token) {
    return (
      <AuthLayout title={t('auth.resetPasswordTitle')} subtitle={t('auth.resetPasswordSubtitle')}>
        <p className={styles.successBox}>{t('auth.invalidResetLink')}</p>
        <p className={styles.switch}><Link to="/forgot-password">{t('auth.requestNewLink')}</Link></p>
      </AuthLayout>
    );
  }

  return (
    <AuthLayout title={t('auth.resetPasswordTitle')} subtitle={t('auth.resetPasswordSubtitle')} serverError={serverError}>
      {done ? (
        <p className={styles.successBox}>{t('auth.resetPasswordSuccess')}</p>
      ) : (
        <form onSubmit={submit} noValidate>
          <FormField label={t('auth.newPasswordLabel')} error={touched.password && errors.password}>
            <PasswordInput value={password} autoComplete="new-password"
              className={inputClass(touched.password && errors.password)}
              onChange={(e) => setPassword(e.target.value)}
              onBlur={() => setTouched((t) => ({ ...t, password: true }))}
              placeholder={t('auth.passwordPlaceholderMin')} />
          </FormField>

          <FormField label={t('auth.confirmPasswordLabel')} error={touched.confirm && errors.confirm}>
            <PasswordInput value={confirm} autoComplete="new-password"
              className={inputClass(touched.confirm && errors.confirm)}
              onChange={(e) => setConfirm(e.target.value)}
              onBlur={() => setTouched((t) => ({ ...t, confirm: true }))}
              placeholder={t('auth.confirmPasswordPlaceholder')} />
          </FormField>

          <Button type="submit" variant="saffron" size="lg" loading={busy} className={styles.submit}>
            {t('auth.resetPasswordSubmit')}
          </Button>
        </form>
      )}
    </AuthLayout>
  );
}
