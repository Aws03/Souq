import { CloseIcon } from '../icons/Icons';
import styles from './Drawer.module.css';

/**
 * درج منزلق عام (Slide-in Panel) — يُستخدم لسلّة المشتريات (من اليسار) ونماذج
 * الإضافة/التعديل في لوحة الإدارة (من اليمين). busy يمنع الإغلاق أثناء الحفظ.
 */
export default function Drawer({ open, onClose, side = 'left', title, busy = false, width = 420, footer, children }) {
  if (!open) return null;

  return (
    <>
      <div className={styles.overlay} onClick={() => !busy && onClose()} />
      <aside className={`${styles.panel} ${styles[side]}`} style={{ width: `min(${width}px, 92vw)` }} role="dialog" aria-modal="true">
        <div className={styles.head}>
          <h3 className={styles.title}>{title}</h3>
          <button type="button" className={styles.close} onClick={onClose} disabled={busy} aria-label="إغلاق">
            <CloseIcon size={18} />
          </button>
        </div>
        <div className={styles.body}>{children}</div>
        {footer && <div className={styles.foot}>{footer}</div>}
      </aside>
    </>
  );
}
