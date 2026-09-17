import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import styles from './PasswordInput.module.css';

// وجه لطيف يغطّي عينيه بيديه — يده اليمنى فقط تنزاح قليلاً عند كشف كلمة المرور
// فتُظهر عيناً واحدة (نمط "peek-a-boo")، بدل أيقونة عين عامة. SVG مضمّن بالكامل
// (بلا مكتبة خارجية) وألوانه من رموز التصميم فتتبدّل تلقائياً مع تبديل السمة.
function PeekFace({ revealed }) {
  return (
    <svg viewBox="0 0 24 24" width="20" height="20" aria-hidden="true" focusable="false">
      <circle cx="12" cy="12" r="10" fill="var(--color-accent-soft)" />
      <path d="M8.5 15.5c1 1 3 1 3.6 0" stroke="var(--color-primary)" strokeWidth="1.2" strokeLinecap="round" fill="none" />

      {/* العينان ثابتتان تحت اليدين دوماً — تنكشف اليمنى فقط حين تتحرّك يدها */}
      <circle cx="8.5" cy="11" r="1.15" fill="var(--color-primary-strong)" />
      <circle cx="15.5" cy="11" r="1.15" fill="var(--color-primary-strong)" />

      {/* اليد اليسرى: تبقى مغطّية دوماً */}
      <g>
        <ellipse cx="8.5" cy="11.2" rx="3" ry="2.7" fill="var(--color-primary)" />
        <path d="M6.1 9.8c.7-.45 1.5-.65 2.3-.65M6.1 12.6c.7.45 1.5.65 2.3.65"
          stroke="var(--color-accent-soft)" strokeWidth=".55" fill="none" strokeLinecap="round" />
      </g>

      {/* اليد اليمنى: الحركة الوحيدة في الأيقونة — تنزاح للأعلى وتدور قليلاً */}
      <g className={`${styles.rightHand} ${revealed ? styles.revealed : ''}`}>
        <ellipse cx="15.5" cy="11.2" rx="3" ry="2.7" fill="var(--color-primary)" />
        <path d="M13.1 9.8c.7-.45 1.5-.65 2.3-.65M13.1 12.6c.7.45 1.5.65 2.3.65"
          stroke="var(--color-accent-soft)" strokeWidth=".55" fill="none" strokeLinecap="round" />
      </g>
    </svg>
  );
}

// إدخال كلمة مرور قابل لإعادة الاستخدام: نفس مظهر حقل عادي (يستقبل نفس
// className من inputClass) لكن بزرّ كشف/إخفاء داخلي بأيقونة الوجه أعلاه.
// حالة الكشف محلّية لكل حقل — كشف واحد لا يكشف البقية أبداً (useState منفصل
// لكل عنصر مستخدَم). موضع الزرّ يسار الحقل فعلياً (لا منطقياً) بحسب الطلب،
// فيبقى ثابتاً هناك بصرف النظر عن اتجاه الصفحة (RTL/LTR).
export default function PasswordInput({ className, ...inputProps }) {
  const { t } = useTranslation();
  const [visible, setVisible] = useState(false);

  return (
    <div className={styles.wrapper}>
      <input
        {...inputProps}
        type={visible ? 'text' : 'password'}
        className={className}
        style={{ paddingLeft: 42 }}
      />
      <button
        type="button"
        className={styles.toggle}
        onClick={() => setVisible((v) => !v)}
        aria-label={t(visible ? 'auth.passwordHide' : 'auth.passwordShow')}
      >
        <PeekFace revealed={visible} />
      </button>
    </div>
  );
}

// FormField يربط تسميته بهذا الحقل كما يربطها بـ <input>: المعرّف والوصف يمرّان إلى الإدخال الداخلي.
PasswordInput.isFormControl = true;
