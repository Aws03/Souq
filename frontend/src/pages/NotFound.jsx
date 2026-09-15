import { Link } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { usePageMetadata } from '../app/usePageMetadata';
import styles from './NotFound.module.css';

// ============================================================================
// صفحة غير موجودة. كانت المسارات المجهولة تُحوَّل بصمت إلى الرئيسية، وهو سلوك يبدو لطيفاً
// ويكلّف فعلياً: الزائر يظنّ أنه وصل، ومحرّكات البحث تفهرس تحويلاً بدل 404، ورابط مكسور في
// حملة إعلانية لا يظهر في أي مكان. الصفحة تقول ما حدث وتعرض مخرجاً.
// ============================================================================
export default function NotFound() {
  const { t } = useTranslation();
  usePageMetadata({ title: t('errors.notFoundTitle'), robots: 'noindex' });

  return (
    <main className={styles.wrap}>
      <p className={styles.code}>404</p>
      <h1 className={styles.title}>{t('errors.notFoundTitle')}</h1>
      <p className={styles.message}>{t('errors.notFoundMessage')}</p>
      <Link to="/" className={styles.action}>{t('errors.boundaryHome')}</Link>
    </main>
  );
}
