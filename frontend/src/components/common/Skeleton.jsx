import styles from './Skeleton.module.css';

// كتلة هيكلية بلمعان خفيف (Shimmer) — تحلّ محل نص "جارٍ التحميل" الخام.
export default function Skeleton({ width = '100%', height = 16, radius = 8, className = '' }) {
  return (
    <span
      className={`${styles.skeleton} ${className}`}
      style={{ width, height, borderRadius: radius }}
      aria-hidden="true"
    />
  );
}
