import { useTranslation } from 'react-i18next';
import { useToast } from '../../context/ToastContext';
import { SuccessIcon, AlertIcon, InfoIcon, CloseIcon } from '../icons/Icons';
import styles from './Toast.module.css';

const ICONS = { success: SuccessIcon, error: AlertIcon, info: InfoIcon };

// حاوية التنبيهات: تُرسم مرة واحدة في جذر التطبيق، وتُكدّس أسفل اليسار.
export default function ToastContainer() {
  const { t } = useTranslation();
  const { toasts, dismiss } = useToast();
  if (!toasts.length) return null;

  return (
    <div className={styles.stack} role="status" aria-live="polite">
      {toasts.map((toast) => {
        const Icon = ICONS[toast.variant] || InfoIcon;
        return (
          <div key={toast.id} className={`${styles.toast} ${styles[toast.variant]}`}>
            <Icon size={18} />
            <span className={styles.msg}>{toast.message}</span>
            <button type="button" className={styles.close} onClick={() => dismiss(toast.id)} aria-label={t('common.close')}>
              <CloseIcon size={14} />
            </button>
          </div>
        );
      })}
    </div>
  );
}
