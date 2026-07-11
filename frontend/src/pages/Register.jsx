import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';

// صفحة إنشاء حساب. نفس منهج التحقّق الفوري. التسجيل ينشئ عميلاً دائماً
// (لا يُنشأ أدمن من هنا) ويسجّل الدخول تلقائياً ثم يوجّه للمتجر.
export default function Register() {
  const { register } = useAuth();
  const navigate = useNavigate();

  const [form, setForm] = useState({ fullName: '', email: '', password: '', confirm: '' });
  const [touched, setTouched] = useState({});
  const [serverError, setServerError] = useState(null);
  const [busy, setBusy] = useState(false);

  const set = (k) => (e) => setForm((f) => ({ ...f, [k]: e.target.value }));
  const blur = (k) => () => setTouched((t) => ({ ...t, [k]: true }));

  const errors = {
    fullName: !form.fullName.trim() ? 'الاسم الكامل مطلوب'
      : form.fullName.trim().length < 3 ? 'الاسم قصير جداً' : null,
    email: !form.email ? 'البريد الإلكتروني مطلوب'
      : !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(form.email) ? 'صيغة البريد غير صحيحة' : null,
    password: !form.password ? 'كلمة المرور مطلوبة'
      : form.password.length < 8 ? 'كلمة المرور 8 أحرف على الأقل' : null,
    confirm: form.confirm !== form.password ? 'كلمتا المرور غير متطابقتين' : null,
  };
  const isValid = !Object.values(errors).some(Boolean);

  const submit = async (e) => {
    e.preventDefault();
    setTouched({ fullName: true, email: true, password: true, confirm: true });
    if (!isValid) return;
    setBusy(true); setServerError(null);
    try {
      await register(form.fullName.trim(), form.email.trim(), form.password);
      navigate('/', { replace: true });   // العميل الجديد يبدأ من المتجر
    } catch (err) {
      setServerError(err.message || 'تعذّر إنشاء الحساب');
    } finally { setBusy(false); }
  };

  const err = (k) => touched[k] && errors[k]
    ? <span className="field-error">{errors[k]}</span> : null;
  const cls = (k) => touched[k] && errors[k] ? 'invalid' : '';

  return (
    <div className="auth-wrap">
      <form className="auth-card" onSubmit={submit} noValidate>
        <div className="auth-brand">سو<span>ق</span></div>
        <h2 className="auth-title">إنشاء حساب</h2>
        <p className="auth-sub">انضمّ إلى سوق وابدأ التسوّق</p>

        {serverError && <div className="auth-alert">⚠ {serverError}</div>}

        <div className="field">
          <label>الاسم الكامل</label>
          <input value={form.fullName} autoComplete="name" className={cls('fullName')}
            onChange={set('fullName')} onBlur={blur('fullName')} placeholder="محمد عبدالله" />
          {err('fullName')}
        </div>

        <div className="field">
          <label>البريد الإلكتروني</label>
          <input type="email" dir="ltr" value={form.email} autoComplete="email" className={cls('email')}
            onChange={set('email')} onBlur={blur('email')} placeholder="you@example.com" />
          {err('email')}
        </div>

        <div className="field">
          <label>كلمة المرور</label>
          <input type="password" value={form.password} autoComplete="new-password" className={cls('password')}
            onChange={set('password')} onBlur={blur('password')} placeholder="8 أحرف على الأقل" />
          {err('password')}
        </div>

        <div className="field">
          <label>تأكيد كلمة المرور</label>
          <input type="password" value={form.confirm} autoComplete="new-password" className={cls('confirm')}
            onChange={set('confirm')} onBlur={blur('confirm')} placeholder="أعد كتابة كلمة المرور" />
          {err('confirm')}
        </div>

        <button className="checkout-btn" type="submit" disabled={busy}>
          {busy ? 'جارٍ الإنشاء…' : 'إنشاء الحساب'}
        </button>

        <p className="auth-switch">
          لديك حساب بالفعل؟ <Link to="/login">سجّل الدخول</Link>
        </p>
      </form>
    </div>
  );
}
