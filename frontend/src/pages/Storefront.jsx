import Hero from '../components/store/Hero';
import CategoryBar from '../components/layout/CategoryBar';
import ProductGrid from '../components/product/ProductGrid';
import styles from './Storefront.module.css';

// صفحة المتجر: بانر + شريط فئات + شبكة منتجات. البحث يأتي جاهزاً من شريط
// التنقّل (خادم حقيقي عبر useProducts)، هذه الصفحة تُركّب العرض فقط.
export default function Storefront({ products, categories, loading, error, onRetry, filter, setFilter, onAdded }) {
  return (
    <>
      <Hero />
      <CategoryBar categories={categories} activeId={filter} onSelect={setFilter} />
      <div className={`souq-layout ${styles.section}`} id="product-grid">
        <ProductGrid products={products} loading={loading} error={error} onRetry={onRetry} onAdded={onAdded} />
      </div>
    </>
  );
}
