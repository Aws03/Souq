import { useTranslation } from 'react-i18next';
import { CloseIcon } from '../icons/Icons';
import { useDialog } from './useDialog';
import styles from './Drawer.module.css';

/**
 * درج منزلق عام (Slide-in Panel) — يُستخدم لسلّة المشتريات (من اليسار) ونماذج
 * الإضافة/التعديل في لوحة الإدارة (من اليمين). busy يمنع الإغلاق أثناء الحفظ.
 */
export default function Drawer({ open, onClose, side = 'left', title, busy = false, width = 420, footer, children }) {
  const { t } = useTranslation();
  // busy = حفظ جارٍ: لا Escape ولا نقر خلفية يغلق أثناءه، كما هو حال زرّ الإغلاق.
  const panelRef = useDialog(open, onClose, { locked: busy });
  if (!open) return null;

  return (
    <>
      {/* الخلفية زرّ حقيقي لا <div> بمستمع نقر: لها اسم يقرؤه قارئ الشاشة، وتُبلَغ بلوحة المفاتيح. */}
      <button type="button" className={styles.overlay} disabled={busy}
        aria-label={t('common.close')} onClick={onClose} />
      {/* عرض الدرج فرق تصميم لكل استدعاء — يُمرَّر كخاصية CSS مخصّصة (لا width
          مباشرة) كي تستطيع media query الجوال في Drawer.module.css تجاوزه
          بعرض كامل الشاشة بلا !important (الأولوية العادية لا تكفي فوق style مباشر). */}
      <aside ref={panelRef} tabIndex={-1} className={`${styles.panel} ${styles[side]}`}
        style={{ '--drawer-width': `${width}px` }} role="dialog" aria-modal="true">
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
