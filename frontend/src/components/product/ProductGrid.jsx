import Skeleton from '../common/Skeleton';
import { EmptyState, ErrorBanner } from '../common/StateViews';
import { PackageIcon } from '../icons/Icons';
import ProductCard from './ProductCard';
import styles from './ProductGrid.module.css';

const SKELETON_COUNT = 8;

// شبكة المنتجات: تتولّى حالات التحميل (هيكل)، الخطأ (لافتة + إعادة محاولة)،
// الفراغ (حالة مصمَّمة)، والعرض الطبيعي — بدل تكرار هذا المنطق في كل صفحة.
export default function ProductGrid({ products, loading, error, onRetry, onAdded }) {
  if (error) return <ErrorBanner message={error} onRetry={onRetry} />;

  if (loading) {
    return (
      <div className={styles.grid}>
        {Array.from({ length: SKELETON_COUNT }).map((_, i) => (
          <div className={styles.skeletonCard} key={i}>
            <Skeleton height={170} radius={0} />
            <div className={styles.skeletonBody}>
              <Skeleton width="40%" height={12} />
              <Skeleton width="80%" height={18} />
              <Skeleton width="60%" height={14} />
            </div>
          </div>
        ))}
      </div>
    );
  }

  if (products.length === 0) {
    return <EmptyState icon={PackageIcon} title="لا منتجات مطابقة" message="جرّب كلمة بحث أو فئة مختلفة." />;
  }

  return (
    <div className={styles.grid}>
      {products.map((p) => <ProductCard key={p.id} product={p} onAdded={onAdded} />)}
    </div>
  );
}
