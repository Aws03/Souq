import { useTranslation } from 'react-i18next';
import { AlertIcon, PackageIcon, RefreshIcon } from '../icons/Icons';
import Button from './Button';
import styles from './StateViews.module.css';

/**
 * الحالة الفارغة الموحّدة لأي قائمة/جدول: رسم توضيحي + رسالة + إجراء اختياري.
 * تُستخدم بدل نص "لا يوجد" الخام في كل مكان (متجر، لوحة الإدارة).
 */
export function EmptyState({ title, message, actionLabel, onAction, icon: Icon = PackageIcon }) {
  return (
    <div className={styles.empty}>
      <div className={styles.emptyIcon}><Icon /></div>
      <h3 className={styles.emptyTitle}>{title}</h3>
      {message && <p className={styles.emptyMsg}>{message}</p>}
      {actionLabel && onAction && (
        <Button variant="saffron" size="sm" onClick={onAction}>{actionLabel}</Button>
      )}
    </div>
  );
}

/** لافتة خطأ داخل الصفحة مع زر إعادة محاولة — بديل عن alert() المتصفح. */
export function ErrorBanner({ message, onRetry }) {
  const { t } = useTranslation();
  return (
    <div className={styles.error} role="alert">
      <AlertIcon size={18} />
      <span className={styles.errorMsg}>{message}</span>
      {onRetry && (
        <button type="button" className={styles.retry} onClick={onRetry}>
          <RefreshIcon size={14} /> {t('common.retry')}
        </button>
      )}
    </div>
  );
}
