import { useTranslation } from 'react-i18next';
import Skeleton from '../common/Skeleton';
import { EmptyState, ErrorBanner } from '../common/StateViews';
import { PackageIcon } from '../icons/Icons';
import ProductCard from './ProductCard';
import styles from './ProductGrid.module.css';

const SKELETON_COUNT = 10;

// شبكة المنتجات: تتولّى حالات التحميل/الخطأ/الفراغ/العرض. view يحدّد التخطيط:
// grid5 (افتراضي: 5 بالصف على سطح المكتب، 2 على الجوال)، grid4 (4 بالصف)، أو
// list (صفّ واحد ببطاقات أفقية).
export default function ProductGrid({ products, loading, error, onRetry, onAdded, view = 'grid5' }) {
  const { t } = useTranslation();
  if (error) return <ErrorBanner message={error} onRetry={onRetry} />;

  const gridClass = `${styles.grid} ${styles[view] || styles.grid5}`;
  const layout = view === 'list' ? 'list' : 'grid';

  if (loading) {
    return (
      <div className={gridClass}>
        {Array.from({ length: SKELETON_COUNT }).map((_, i) => (
          <div className={styles.skeletonCard} key={i}>
            <Skeleton height={180} radius={0} />
            <div className={styles.skeletonBody}>
              <Skeleton width="80%" height={16} />
              <Skeleton width="50%" height={14} />
            </div>
          </div>
        ))}
      </div>
    );
  }

  if (products.length === 0) {
    return <EmptyState icon={PackageIcon} title={t('product.noMatchTitle')} message={t('product.noMatchMessage')} />;
  }

  return (
    <div className={gridClass}>
      {products.map((p) => <ProductCard key={p.id} product={p} onAdded={onAdded} layout={layout} />)}
    </div>
  );
}
