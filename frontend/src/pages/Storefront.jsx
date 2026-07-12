import { useSearchParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import Hero from '../components/store/Hero';
import ProductSection from '../components/store/ProductSection';
import Catalog from '../components/catalog/Catalog';
import styles from './Storefront.module.css';

// الصفحة الرئيسية: بانر + ثلاثة صفوف تمرير أفقي (أحدث/أكثر مبيعاً/عروض) ثم
// الكتالوج الكامل (شبكة قابلة للفلترة والترقيم). حين يوجد أي فلتر/بحث نشط في
// الرابط نُخفي الأقسام الترويجية وتصبح الصفحة قائمة كتالوج مفلترة نظيفة —
// فالنقر على فئة من الشريط العلوي (?cats=id) يقود لعرض تلك الفئة وحدها.
export default function Storefront({
  categories, onAdded, searchTerm, refreshKey,
  newArrivals, newArrivalsLoading, bestSellers, bestSellersLoading, offers, offersLoading,
}) {
  const { t } = useTranslation();
  const [searchParams] = useSearchParams();

  const hasActiveView = ['cats', 'min', 'max', 'sort', 'page'].some((k) => searchParams.get(k)) || !!searchTerm;
  const showSections = !hasActiveView;

  return (
    <>
      {showSections && (
        <>
          <Hero targetId="catalog" />
          <ProductSection title={t('store.newArrivals')} products={newArrivals} loading={newArrivalsLoading}
            onAdded={onAdded} isNew viewAllTargetId="catalog" />
          <ProductSection title={t('store.bestSellers')} products={bestSellers} loading={bestSellersLoading}
            onAdded={onAdded} viewAllTargetId="catalog" />
          <ProductSection title={t('nav.offers')} products={offers} loading={offersLoading}
            onAdded={onAdded} viewAllTargetId="catalog" />
        </>
      )}

      <div className={`souq-layout ${styles.section}`} id="catalog">
        <h2 className={styles.allProductsTitle}>{t('store.allProducts')}</h2>
        <Catalog categories={categories} searchTerm={searchTerm} onAdded={onAdded} refreshKey={refreshKey} />
      </div>
    </>
  );
}
