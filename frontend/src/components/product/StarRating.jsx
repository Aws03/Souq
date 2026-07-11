import { StarIcon } from '../icons/Icons';
import styles from './StarRating.module.css';

// نجوم تقييم: عرض فقط (average) أو اختيار تفاعلي (value + onChange في نموذج التقييم).
export default function StarRating({ value, onChange, size = 16 }) {
  const interactive = !!onChange;
  const stars = [1, 2, 3, 4, 5];

  return (
    <span className={styles.stars} role={interactive ? 'radiogroup' : undefined} aria-label="التقييم">
      {stars.map((n) => (
        <button
          key={n}
          type="button"
          className={`${styles.star} ${n <= Math.round(value) ? styles.filled : ''}`}
          onClick={interactive ? () => onChange(n) : undefined}
          disabled={!interactive}
          aria-label={`${n} من 5`}
        >
          <StarIcon size={size} filled={n <= Math.round(value)} />
        </button>
      ))}
    </span>
  );
}
