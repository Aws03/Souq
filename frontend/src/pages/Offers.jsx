import { useOutletContext } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import Catalog from '../components/catalog/Catalog';
import styles from './Offers.module.css';
import { usePageMetadata } from '../app/usePageMetadata';

// صفحة العروض: الكتالوج نفسه مفلتراً على المنتجات المخفّضة فعلاً (onSale في الخادم:
// سعر مقارنة أعلى من السعر). كانت تعرض كل المنتجات بترتيب الأحدث باسم "عروض".
export default function Offers() {
  const { t } = useTranslation();
  const { showToast, refreshKey, categories } = useOutletContext();
  usePageMetadata({ title: t('offers.title'), description: t('offers.subtitle') });

  return (
    <div className={`souq-layout ${styles.page}`}>
      <h1 className={styles.title}>{t('offers.title')}</h1>
      <p className={styles.subtitle}>{t('offers.subtitle')}</p>
      <Catalog categories={categories} onAdded={showToast} refreshKey={refreshKey} onSale />
    </div>
  );
}
