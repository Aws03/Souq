import styles from './FormField.module.css';

/** يبني اسم صنف الحقل (عادي/غير صالح) — يُستخدم على عنصر الإدخال الفعلي داخل FormField. */
export const inputClass = (hasError, extra = '') =>
  `${styles.input} ${hasError ? styles.invalid : ''} ${extra}`.trim();

// غلاف موحّد لكل حقل نموذج: تسمية + عنصر الإدخال (يمرَّره المستدعي) + رسالة خطأ.
export default function FormField({ label, htmlFor, error, hint, children }) {
  return (
    <div className={styles.field}>
      {label && <label className={styles.label} htmlFor={htmlFor}>{label}</label>}
      {children}
      {error ? <span className={styles.error}>{error}</span>
        : hint ? <span className={styles.hint}>{hint}</span> : null}
    </div>
  );
}
