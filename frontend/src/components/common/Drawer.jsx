import { useTranslation } from 'react-i18next';
import { CloseIcon } from '../icons/Icons';
import styles from './Drawer.module.css';

/**
 * درج منزلق عام (Slide-in Panel) — يُستخدم لسلّة المشتريات (من اليسار) ونماذج
 * الإضافة/التعديل في لوحة الإدارة (من اليمين). busy يمنع الإغلاق أثناء الحفظ.
 */
export default function Drawer({ open, onClose, side = 'left', title, busy = false, width = 420, footer, children }) {
  const { t } = useTranslation();
  if (!open) return null;

  return (
    <>
      <div className={styles.overlay} onClick={() => !busy && onClose()} />
      {/* عرض الدرج فرق تصميم لكل استدعاء — يُمرَّر كخاصية CSS مخصّصة (لا width
          مباشرة) كي تستطيع media query الجوال في Drawer.module.css تجاوزه
          بعرض كامل الشاشة بلا !important (الأولوية العادية لا تكفي فوق style مباشر). */}
      <aside className={`${styles.panel} ${styles[side]}`} style={{ '--drawer-width': `${width}px` }} role="dialog" aria-modal="true">
        <div className={styles.head}>
          <h3 className={styles.title}>{title}</h3>
          <button type="button" className={styles.close} onClick={onClose} disabled={busy} aria-label={t('common.close')}>
            <CloseIcon size={18} />
          </button>
        </div>
        <div className={styles.body}>{children}</div>
        {footer && <div className={styles.foot}>{footer}</div>}
      </aside>
    </>
  );
}
