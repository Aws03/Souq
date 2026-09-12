import { useTranslation } from 'react-i18next';
import Spinner from '../components/common/Spinner';
import { storeName } from './tenantModel';
import styles from './BootScreens.module.css';

// شاشات ما قبل المتجر (المرحلة 15): تحميل إعداده، متجر مغلق، مضيف لا متجر عليه، أو تعذّر الاتصال مع إعادة المحاولة.
// متجر مغلق يصل ومعه إعداده (R-08)، فتحمل شاشته اسمه — وألوانه وخطّه مطبَّقان أصلاً من TenantProvider. أما المضيف
// المجهول وخطأ الشبكة فلا إعداد لهما: نصوص عامة بلغة الزائر.
export function BootScreen({ mode, config, onRetry }) {
  const { t, i18n } = useTranslation();
  if (mode === 'loading') {
    return <div className={styles.screen} aria-busy="true"><Spinner size={32} /></div>;
  }

  const key = mode === 'closed' || mode === 'unknown' ? mode : 'error';
  const name = key === 'closed' ? storeName(config, i18n.language) : '';
  return (
    <main className={styles.screen}>
      <div className={styles.card}>
        {name && <p className={styles.message}><strong>{name}</strong></p>}
        <h1 className={styles.title}>{t(`boot.${key}.title`)}</h1>
        <p className={styles.message}>{t(`boot.${key}.message`)}</p>
        {key === 'error' && (
          <button type="button" className={styles.retry} onClick={onRetry}>{t('boot.retry')}</button>
        )}
      </div>
    </main>
  );
}
