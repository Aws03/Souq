import StarRating from '../product/StarRating';
import { EmptyState, ErrorBanner } from '../common/StateViews';
import Skeleton from '../common/Skeleton';
import { StarIcon } from '../icons/Icons';
import styles from './ReviewList.module.css';

// قائمة تقييمات منتج: هيكل تحميل، حالة فارغة، لافتة خطأ، ثم بطاقة لكل تقييم.
export default function ReviewList({ reviews, loading, error, onRetry }) {
  if (error) return <ErrorBanner message={error} onRetry={onRetry} />;

  if (loading) {
    return (
      <div className={styles.list}>
        {Array.from({ length: 3 }).map((_, i) => (
          <div className={styles.item} key={i}>
            <Skeleton width={120} height={14} />
            <Skeleton width="90%" height={14} />
            <Skeleton width="60%" height={14} />
          </div>
        ))}
      </div>
    );
  }

  if (reviews.length === 0) {
    return <EmptyState icon={StarIcon} title="لا تقييمات بعد" message="كن أول من يقيّم هذا المنتج." />;
  }

  return (
    <div className={styles.list}>
      {reviews.map((r) => (
        <div className={styles.item} key={r.id}>
          <div className={styles.head}>
            <span className={styles.name}>{r.customerName}</span>
            <StarRating value={r.rating} />
          </div>
          <p className={styles.comment}>{r.comment}</p>
          <span className={styles.date}>{new Date(r.createdAt).toLocaleDateString('ar-JO')}</span>
        </div>
      ))}
    </div>
  );
}
