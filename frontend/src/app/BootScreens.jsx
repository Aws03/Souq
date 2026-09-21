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

  // المتجر المؤرشف وحده يبلغ هذه الشاشة الآن (C3): الموقوف وقيد التجهيز يُركَّب تطبيقهما، فرسالتهما
  // داخله. ولذلك تفترق رسالة الأرشفة عن "مغلق مؤقتاً" — كانت الثلاث رسالةً واحدة تقول لمن أُغلق
  // متجره نهائياً إننا "نعود قريباً".
  const closed = mode === 'closed' ? (config?.status === 'Archived' ? 'archived' : 'closed') : null;
  const key = closed ?? (mode === 'unknown' ? 'unknown' : 'error');
  const name = closed === null ? '' : storeName(config, i18n.language);
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
