import { useToast } from '../../context/ToastContext';
import { SuccessIcon, AlertIcon, InfoIcon, CloseIcon } from '../icons/Icons';
import styles from './Toast.module.css';

const ICONS = { success: SuccessIcon, error: AlertIcon, info: InfoIcon };

// حاوية التنبيهات: تُرسم مرة واحدة في جذر التطبيق، وتُكدّس أسفل اليسار.
export default function ToastContainer() {
  const { toasts, dismiss } = useToast();
  if (!toasts.length) return null;

  return (
    <div className={styles.stack} role="status" aria-live="polite">
      {toasts.map((t) => {
        const Icon = ICONS[t.variant] || InfoIcon;
        return (
          <div key={t.id} className={`${styles.toast} ${styles[t.variant]}`}>
            <Icon size={18} />
            <span className={styles.msg}>{t.message}</span>
            <button type="button" className={styles.close} onClick={() => dismiss(t.id)} aria-label="إغلاق">
              <CloseIcon size={14} />
            </button>
          </div>
        );
      })}
    </div>
  );
}
