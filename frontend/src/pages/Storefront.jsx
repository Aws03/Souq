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
  categories, onAdded, refreshKey,
  newArrivals, newArrivalsLoading, bestSellers, bestSellersLoading, offers, offersLoading,
}) {
  const { t } = useTranslation();
  const [searchParams] = useSearchParams();

  // 'q' ضمنها الآن: البحث حالة رابط كبقية الفلاتر (searchRouting).
  const hasActiveView = ['cats', 'min', 'max', 'sort', 'page', 'q'].some((k) => searchParams.get(k));
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
          {/* صفّ العروض: منتجات مخفّضة فعلاً (onSale في الخادم). ProductSection يخفي نفسه
              حين لا نتائج، فمتجر بلا تخفيضات لا يعرض صفّاً اسمه "عروض" فيه منتجات عادية. */}
          <ProductSection title={t('nav.offers')} products={offers} loading={offersLoading}
            onAdded={onAdded} viewAllHref="/offers" />
        </>
      )}

      <div className={`souq-layout ${styles.section}`} id="catalog">
        <h2 className={styles.allProductsTitle}>{t('store.allProducts')}</h2>
        <Catalog categories={categories} onAdded={onAdded} refreshKey={refreshKey} />
      </div>
    </>
  );
}
