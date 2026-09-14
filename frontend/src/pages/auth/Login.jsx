import { useState } from 'react';
import { Link, useNavigate, useLocation } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useAuth, canManageStore } from '../../context/AuthContext';
import { safeRedirect } from '../../features/auth/safeRedirect';
import FormField, { inputClass } from '../../components/common/FormField';
import PasswordInput from '../../components/common/PasswordInput';
import Button from '../../components/common/Button';
import AuthLayout from './AuthLayout';
import styles from './Auth.module.css';

// صفحة الدخول. تحقّق فوري لكل حقل + رسالة خطأ واضحة من الخادم. عند النجاح:
// الأدمن يهبط في لوحة الإدارة، والعميل يعود لوجهته الأصلية أو المتجر.
export default function Login() {
  const { t } = useTranslation();
  const { login } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  // الوجهة يختارها الزائر (ProtectedRoute ينقل الموقع كما طلبه)، فتُفحص قبل استعمالها: تحويل خارجي هنا يقع
  // بعد نجاح الدخول مباشرةً. انظر safeRedirect.
  const from = safeRedirect(location.state?.from?.pathname);

  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [touched, setTouched] = useState({});
  const [serverError, setServerError] = useState(null);
  const [busy, setBusy] = useState(false);

  const errors = {
    email: !email ? t('auth.emailRequired')
      : !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email) ? t('auth.emailInvalid') : null,
    password: !password ? t('auth.passwordRequired') : null,
  };
  const isValid = !errors.email && !errors.password;

  const submit = async (e) => {
    e.preventDefault();
    setTouched({ email: true, password: true });
    if (!isValid) return;
    setBusy(true); setServerError(null);
    try {
      const user = await login(email.trim(), password);
      navigate(canManageStore(user) ? '/admin' : from, { replace: true });
    } catch (err) {
      setServerError(err.message || t('auth.loginFailed'));
    } finally { setBusy(false); }
  };

  return (
    <AuthLayout title={t('auth.loginTitle')} subtitle={t('auth.loginSubtitle')} serverError={serverError}>
      <form onSubmit={submit} noValidate>
        <FormField label={t('auth.emailLabel')} error={touched.email && errors.email}>
          <input type="email" dir="ltr" value={email} autoComplete="email"
            className={inputClass(touched.email && errors.email)}
            onChange={(e) => setEmail(e.target.value)}
            onBlur={() => setTouched((t) => ({ ...t, email: true }))}
            placeholder="you@example.com" />
        </FormField>

        <FormField label={t('auth.passwordLabel')} error={touched.password && errors.password}>
          <PasswordInput value={password} autoComplete="current-password"
            className={inputClass(touched.password && errors.password)}
            onChange={(e) => setPassword(e.target.value)}
            onBlur={() => setTouched((t) => ({ ...t, password: true }))}
            placeholder="••••••••" />
        </FormField>

        <p className={styles.forgotLink}><Link to="/forgot-password">{t('auth.forgotPasswordLink')}</Link></p>

        <Button type="submit" variant="saffron" size="lg" loading={busy} className={styles.submit}>{t('auth.loginSubmit')}</Button>

        <p className={styles.switch}>{t('auth.noAccount')} <Link to="/register">{t('auth.createAccount')}</Link></p>
      </form>
    </AuthLayout>
  );
}
