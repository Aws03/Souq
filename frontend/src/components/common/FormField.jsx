import { Children, cloneElement, isValidElement, useId } from 'react';
import styles from './FormField.module.css';

/** يبني اسم صنف الحقل (عادي/غير صالح) — يُستخدم على عنصر الإدخال الفعلي داخل FormField. */
export const inputClass = (hasError, extra = '') =>
  `${styles.input} ${hasError ? styles.invalid : ''} ${extra}`.trim();

// ما يُربط بالتسمية تلقائياً: عناصر إدخال حقيقية، ومكوّن يعلن أنه حقل (isFormControl، مثل PasswordInput) —
// علامة لا استيراد، كي لا يجرّ هذا الملف المستعمل في كل مكان كل مكوّن حقل إلى التحميل الأوّل.
// غلافٌ (صفّ مربّع اختيار داخل <label>) يبقى كما هو — تسمية تشير إلى <div> أسوأ من لا تسمية.
const NATIVE_CONTROLS = new Set(['input', 'select', 'textarea']);
const isControl = (type) => NATIVE_CONTROLS.has(type) || type?.isFormControl === true;

// ============================================================================
// غلاف موحّد لكل حقل نموذج: تسمية + عنصر الإدخال (يمرَّره المستدعي) + رسالة خطأ أو تلميح.
//
// كانت التسمية لا تُربط بالحقل إلا حين يمرّر المستدعي htmlFor ومعرّفاً بيده — ولم تفعل ذلك صفحات الدخول
// والتسجيل واستعادة كلمة المرور وقبول الدعوة. فقارئ الشاشة كان يعلن "حقل كلمة مرور" بلا اسم في أوّل
// شاشة يراها مدير متجر جديد. الآن يُولَّد المعرّف ويُمرَّر للحقل، وتُربط الرسالة به بـ aria-describedby.
// مستدعٍ يمرّر htmlFor يبقى صاحب القرار.
// ============================================================================
export default function FormField({ label, htmlFor, error, hint, children }) {
  const generated = useId();
  const child = Children.count(children) === 1 && isValidElement(children) ? children : null;
  const wires = !htmlFor && child !== null && isControl(child.type);
  const id = htmlFor ?? (wires ? child.props.id ?? generated : undefined);
  const message = error || hint;
  const messageId = message && id ? `${id}-message` : undefined;

  const control = wires
    ? cloneElement(child, {
      id,
      'aria-invalid': child.props['aria-invalid'] ?? (error ? true : undefined),
      'aria-describedby': [child.props['aria-describedby'], messageId].filter(Boolean).join(' ') || undefined,
    })
    : children;

  return (
    <div className={styles.field}>
      {label && <label className={styles.label} htmlFor={id}>{label}</label>}
      {control}
      {error ? <span id={messageId} className={styles.error}>{error}</span>
        : hint ? <span id={messageId} className={styles.hint}>{hint}</span> : null}
    </div>
  );
}
