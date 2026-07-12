import { useOutletContext } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import Catalog from '../components/catalog/Catalog';
import styles from './Offers.module.css';

// صفحة العروض: نفس تخطيط الكتالوج تماماً، لكن مثبَّتة على ترتيب الأحدث كبديل
// مؤقّت حتى تُضاف راية "عرض" حقيقية للمنتجات (عندها نفلتر بها هنا فقط).
export default function Offers() {
  const { t } = useTranslation();
  const { showToast, refreshKey, searchTerm, categories } = useOutletContext();

  return (
    <div className={`souq-layout ${styles.page}`}>
      <h1 className={styles.title}>{t('offers.title')}</h1>
      <p className={styles.subtitle}>{t('offers.subtitle')}</p>
      <Catalog categories={categories} searchTerm={searchTerm} onAdded={showToast}
        refreshKey={refreshKey} lockSort="newest" />
    </div>
  );
}
