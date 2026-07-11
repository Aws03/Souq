import styles from './ProductBadges.module.css';

/** يهيّئ السعر بصيغة الدينار الأردني (٣ خانات عشرية: فلس)، مثال: 59.900 د.أ */
export function formatPrice(amount, currency = 'JOD') {
  if (currency === 'JOD') return `${Number(amount).toFixed(3)} د.أ`;
  return `${Number(amount).toFixed(2)} ${currency}`;
}

export function PriceTag({ amount, currency }) {
  return <span className={styles.price}>{formatPrice(amount, currency)}</span>;
}

export function CategoryBadge({ name }) {
  if (!name) return null;
  return <span className={styles.category}>{name}</span>;
}

/** شارة المخزون: "نفد" حين صفر، "باقٍ N" حين المخزون منخفض، وإلا لا شيء. */
export function StockBadge({ quantity, lowThreshold = 5 }) {
  if (quantity <= 0) return <span className={`${styles.stock} ${styles.out}`}>نفد</span>;
  if (quantity <= lowThreshold) return <span className={`${styles.stock} ${styles.low}`}>باقٍ {quantity}</span>;
  return null;
}
