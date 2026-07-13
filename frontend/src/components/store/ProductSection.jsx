import { useTranslation } from 'react-i18next';
import ProductCard from '../product/ProductCard';
import Skeleton from '../common/Skeleton';
import styles from './ProductSection.module.css';

const SKELETON_COUNT = 4;

// صف منتجات عام بعنوان + رابط "عرض الكل" — يُستخدم لكل من "وصل حديثاً" و
// "الأكثر مبيعاً". تمرير أفقي على الجوال، شبكة ثابتة على سطح المكتب.
export default function ProductSection({ title, products, loading, onAdded, isNew = false, viewAllTargetId = 'catalog', showViewAll = true }) {
  const { t } = useTranslation();

  const scrollToAll = () => {
    document.getElementById(viewAllTargetId)?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  };

  if (!loading && (!products || products.length === 0)) return null;

  return (
    <section className={styles.productSection}>
      <div className="souq-layout">
        <div className={styles.productSection__head}>
          <h2 className={styles.productSection__title}>{title}</h2>
          {showViewAll && (
            <button type="button" className={styles.productSection__viewAll} onClick={scrollToAll}>
              {t('store.viewAll')}
            </button>
          )}
        </div>

        <div className={styles.productSection__row}>
          {loading
            ? Array.from({ length: SKELETON_COUNT }).map((_, i) => (
                <div className={styles.productSection__skeletonCard} key={i}>
                  <Skeleton height={170} radius={0} />
                  <div className={styles.productSection__skeletonBody}>
                    <Skeleton width="40%" height={12} />
                    <Skeleton width="80%" height={18} />
                  </div>
                </div>
              ))
            : products.map((p) => (
                <div className={styles.productSection__item} key={p.id}>
                  <ProductCard product={p} onAdded={onAdded} isNew={isNew} />
                </div>
              ))}
        </div>
      </div>
    </section>
  );
}
