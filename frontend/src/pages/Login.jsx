import { useState } from 'react';
import { Link, useNavigate, useLocation } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';

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
      // نوجّه حسب الدور: الأدمن للوحة الإدارة، غيره لوجهته أو المتجر.
      navigate(user?.role === 'Admin' ? '/admin' : (from && from !== '/login' ? from : '/'),
        { replace: true });
    } catch (err) {
      setServerError(err.message || 'تعذّر تسجيل الدخول');
    } finally { setBusy(false); }
  };

  return (
    <div className="auth-wrap">
      <form className="auth-card" onSubmit={submit} noValidate>
        <div className="auth-brand">سو<span>ق</span></div>
        <h2 className="auth-title">تسجيل الدخول</h2>
        <p className="auth-sub">أهلاً بعودتك — ادخل لمتابعة التسوّق</p>

        {serverError && <div className="auth-alert">⚠ {serverError}</div>}

        <div className="field">
          <label>البريد الإلكتروني</label>
          <input type="email" dir="ltr" value={email} autoComplete="email"
            className={touched.email && errors.email ? 'invalid' : ''}
            onChange={(e) => setEmail(e.target.value)}
            onBlur={() => setTouched((t) => ({ ...t, email: true }))}
            placeholder="you@example.com" />
          {touched.email && errors.email && <span className="field-error">{errors.email}</span>}
        </div>

        <div className="field">
          <label>كلمة المرور</label>
          <input type="password" value={password} autoComplete="current-password"
            className={touched.password && errors.password ? 'invalid' : ''}
            onChange={(e) => setPassword(e.target.value)}
            onBlur={() => setTouched((t) => ({ ...t, password: true }))}
            placeholder="••••••••" />
          {touched.password && errors.password && <span className="field-error">{errors.password}</span>}
        </div>

        <button className="checkout-btn" type="submit" disabled={busy}>
          {busy ? 'جارٍ الدخول…' : 'دخول'}
        </button>

        <p className="auth-switch">
          ليس لديك حساب؟ <Link to="/register">أنشئ حساباً</Link>
        </p>
      </form>
    </div>
  );
}
