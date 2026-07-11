import { useTranslation } from 'react-i18next';
import Hero from '../components/store/Hero';
import CategoryGrid from '../components/store/CategoryGrid';
import ProductSection from '../components/store/ProductSection';
import PromoBanner from '../components/store/PromoBanner';
import CategoryBar from '../components/layout/CategoryBar';
import ProductGrid from '../components/product/ProductGrid';
import styles from './Storefront.module.css';

// صفحة المتجر (الرئيسية): بانر شرائح، بطاقات فئات، صفّا منتجات مكتشفة
// ("وصل حديثاً"/"الأكثر مبيعاً") يفصل بينهما شريطا ترويج بتخطيط متبادل، ثم
// شبكة الكتالوج الكاملة القابلة للبحث والتصفية بالفئة.
export default function Storefront({
  products, categories, loading, error, onRetry, filter, setFilter, onAdded,
  newArrivals, newArrivalsLoading, bestSellers, bestSellersLoading,
}) {
  const { t } = useTranslation();

  const scrollToGrid = () => {
    document.getElementById('product-grid')?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  };

  return (
    <>
      <Hero />
      <CategoryGrid categories={categories} onSelect={setFilter} />

      <ProductSection
        title={t('store.newArrivals')} products={newArrivals} loading={newArrivalsLoading}
        onAdded={onAdded} isNew
      />

      <PromoBanner
        headline={t('store.promo1Headline')} subline={t('store.promo1Subline')}
        ctaLabel={t('store.heroCta')} onCtaClick={scrollToGrid} variant={0}
      />

      <ProductSection
        title={t('store.bestSellers')} products={bestSellers} loading={bestSellersLoading}
        onAdded={onAdded}
      />

      <PromoBanner
        headline={t('store.promo2Headline')} subline={t('store.promo2Subline')}
        ctaLabel={t('store.heroCta')} onCtaClick={scrollToGrid} reverse variant={1}
      />

      <CategoryBar categories={categories} activeId={filter} onSelect={setFilter} />
      <div className={`souq-layout ${styles.section}`} id="product-grid">
        <h2 className={styles.allProductsTitle}>{t('store.allProducts')}</h2>
        <ProductGrid products={products} loading={loading} error={error} onRetry={onRetry} onAdded={onAdded} />
      </div>
    </>
  );
}
