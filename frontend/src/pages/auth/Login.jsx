import { useState } from 'react';
import { Link, useNavigate, useLocation } from 'react-router-dom';
import { useAuth } from '../../context/AuthContext';
import FormField, { inputClass } from '../../components/common/FormField';
import Button from '../../components/common/Button';
import AuthLayout from './AuthLayout';
import styles from './Auth.module.css';

// صفحة الدخول. تحقّق فوري لكل حقل + رسالة خطأ واضحة من الخادم. عند النجاح:
// الأدمن يهبط في لوحة الإدارة، والعميل يعود لوجهته الأصلية أو المتجر.
export default function Login() {
  const { login } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const from = location.state?.from?.pathname;

  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [touched, setTouched] = useState({});
  const [serverError, setServerError] = useState(null);
  const [busy, setBusy] = useState(false);

  const errors = {
    email: !email ? 'البريد الإلكتروني مطلوب'
      : !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email) ? 'صيغة البريد غير صحيحة' : null,
    password: !password ? 'كلمة المرور مطلوبة' : null,
  };
  const isValid = !errors.email && !errors.password;

  const submit = async (e) => {
    e.preventDefault();
    setTouched({ email: true, password: true });
    if (!isValid) return;
    setBusy(true); setServerError(null);
    try {
      const user = await login(email.trim(), password);
      navigate(user?.role === 'Admin' ? '/admin' : (from && from !== '/login' ? from : '/'), { replace: true });
    } catch (err) {
      setServerError(err.message || 'تعذّر تسجيل الدخول');
    } finally { setBusy(false); }
  };

  return (
    <AuthLayout title="تسجيل الدخول" subtitle="أهلاً بعودتك — ادخل لمتابعة التسوّق" serverError={serverError}>
      <form onSubmit={submit} noValidate>
        <FormField label="البريد الإلكتروني" error={touched.email && errors.email}>
          <input type="email" dir="ltr" value={email} autoComplete="email"
            className={inputClass(touched.email && errors.email)}
            onChange={(e) => setEmail(e.target.value)}
            onBlur={() => setTouched((t) => ({ ...t, email: true }))}
            placeholder="you@example.com" />
        </FormField>

        <FormField label="كلمة المرور" error={touched.password && errors.password}>
          <input type="password" value={password} autoComplete="current-password"
            className={inputClass(touched.password && errors.password)}
            onChange={(e) => setPassword(e.target.value)}
            onBlur={() => setTouched((t) => ({ ...t, password: true }))}
            placeholder="••••••••" />
        </FormField>

        <Button type="submit" variant="saffron" size="lg" loading={busy} className={styles.submit}>دخول</Button>

        <p className={styles.switch}>ليس لديك حساب؟ <Link to="/register">أنشئ حساباً</Link></p>
      </form>
    </AuthLayout>
  );
}
