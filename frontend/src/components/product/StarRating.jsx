import { useTranslation } from 'react-i18next';
import { StarIcon } from '../icons/Icons';
import styles from './StarRating.module.css';

// نجوم تقييم: عرض فقط (average) أو اختيار تفاعلي (value + onChange في نموذج التقييم).
export default function StarRating({ value, onChange, size = 16 }) {
  const { t } = useTranslation();
  const interactive = !!onChange;
  const stars = [1, 2, 3, 4, 5];

  return (
    <span className={styles.stars} role={interactive ? 'radiogroup' : undefined} aria-label={t('product.ratingAria')}>
      {stars.map((n) => (
        <button
          key={n}
          type="button"
          className={`${styles.star} ${n <= Math.round(value) ? styles.filled : ''}`}
          onClick={interactive ? () => onChange(n) : undefined}
          disabled={!interactive}
          aria-label={t('product.starAria', { n })}
        >
          <StarIcon size={size} filled={n <= Math.round(value)} />
        </button>
      ))}
    </span>
  );
}
