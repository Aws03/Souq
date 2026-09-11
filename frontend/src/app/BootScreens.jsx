import { useTranslation } from 'react-i18next';
import Spinner from '../components/common/Spinner';
import styles from './BootScreens.module.css';

// شاشات ما قبل المتجر (المرحلة 15): تحميل إعداده، متجر موقوف (503)، مضيف لا متجر عليه، أو تعذّر الاتصال مع إعادة المحاولة.
// بلا هوية متجر بعد — نصوص عامة بلغة الزائر.
export function BootScreen({ mode, onRetry }) {
  const { t } = useTranslation();
  if (mode === 'loading') {
    return <div className={styles.screen} aria-busy="true"><Spinner size={32} /></div>;
  }

  const key = mode === 'closed' || mode === 'unknown' ? mode : 'error';
  return (
    <main className={styles.screen}>
      <div className={styles.card}>
        <h1 className={styles.title}>{t(`boot.${key}.title`)}</h1>
        <p className={styles.message}>{t(`boot.${key}.message`)}</p>
        {key === 'error' && (
          <button type="button" className={styles.retry} onClick={onRetry}>{t('boot.retry')}</button>
        )}
      </div>
    </main>
  );
}
