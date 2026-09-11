import { useTranslation } from 'react-i18next';
import StarRating from '../product/StarRating';
import { distributionRows } from '../../features/reviews/ratingSummary';
import styles from './RatingSummary.module.css';

// ملخّص التقييمات المعتمدة (المرحلة 13): المتوسط والعدد وشريط لكل نجمة. لا شيء قبل أول تقييم منشور.
export default function RatingSummary({ average, total, distribution }) {
  const { t } = useTranslation();
  if (!total) return null;

  return (
    <div className={styles.summary}>
      <div className={styles.score}>
        <strong className={styles.average}>{average}</strong>
        <StarRating value={average} />
        <span className={styles.count}>{t('product.ratingSummary', { count: total })}</span>
      </div>
      <ul className={styles.bars} aria-label={t('reviews.distributionAria')}>
        {distributionRows(distribution, total).map(({ rating, count, percent }) => (
          <li key={rating} className={styles.row}>
            <span>{t('reviews.starsLabel', { n: rating })}</span>
            <span className={styles.track}><span className={styles.fill} style={{ width: `${percent}%` }} /></span>
            <span className={styles.value}>{count}</span>
          </li>
        ))}
      </ul>
    </div>
  );
}
