import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useAuth } from '../../context/AuthContext';
import FormField, { inputClass } from '../../components/common/FormField';
import Button from '../../components/common/Button';
import AuthLayout from './AuthLayout';
import styles from './Auth.module.css';

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
      navigate('/', { replace: true });
    } catch (err) {
      setServerError(err.message || 'تعذّر إنشاء الحساب');
    } finally { setBusy(false); }
  };

  const err = (k) => (touched[k] ? errors[k] : null);

  return (
    <AuthLayout title="إنشاء حساب" subtitle="انضمّ إلى ماركة وابدأ التسوّق" serverError={serverError}>
      <form onSubmit={submit} noValidate>
        <FormField label="الاسم الكامل" error={err('fullName')}>
          <input value={form.fullName} autoComplete="name" className={inputClass(err('fullName'))}
            onChange={set('fullName')} onBlur={blur('fullName')} placeholder="محمد عبدالله" />
        </FormField>

        <FormField label="البريد الإلكتروني" error={err('email')}>
          <input type="email" dir="ltr" value={form.email} autoComplete="email" className={inputClass(err('email'))}
            onChange={set('email')} onBlur={blur('email')} placeholder="you@example.com" />
        </FormField>

        <FormField label="كلمة المرور" error={err('password')}>
          <input type="password" value={form.password} autoComplete="new-password" className={inputClass(err('password'))}
            onChange={set('password')} onBlur={blur('password')} placeholder="8 أحرف على الأقل" />
        </FormField>

        <FormField label="تأكيد كلمة المرور" error={err('confirm')}>
          <input type="password" value={form.confirm} autoComplete="new-password" className={inputClass(err('confirm'))}
            onChange={set('confirm')} onBlur={blur('confirm')} placeholder="أعد كتابة كلمة المرور" />
        </FormField>

        <Button type="submit" variant="saffron" size="lg" loading={busy} className={styles.submit}>إنشاء الحساب</Button>

        <p className={styles.switch}>لديك حساب بالفعل؟ <Link to="/login">سجّل الدخول</Link></p>
      </form>
    </AuthLayout>
  );
}
