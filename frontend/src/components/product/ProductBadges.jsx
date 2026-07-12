import { useTranslation } from 'react-i18next';
import i18n from '../../i18n';
import styles from './ProductBadges.module.css';

/**
 * اسم المنتج بلغة الواجهة الحالية. يقبل شكل الـ API الثنائي اللغة
 * (nameAr/nameEn) ويتساقط إلى name القديم دفاعاً عن عناصر سلة/مفضّلة
 * محفوظة في localStorage من قبل هذا التحديث (بلا الحقلين الجديدين).
 */
export function getProductName(product) {
  if (!product) return '';
  if (i18n.language === 'ar') return product.nameAr ?? product.name ?? '';
  return product.nameEn ?? product.nameAr ?? product.name ?? '';
}

/** يهيّئ السعر بصيغة الدينار الأردني (٣ خانات عشرية: فلس)، مثال: 59.900 د.أ / 59.900 JOD */
export function formatPrice(amount, currency = 'JOD') {
  if (currency === 'JOD') {
    const symbol = i18n.language === 'ar' ? 'د.أ' : 'JOD';
    return `${Number(amount).toFixed(3)} ${symbol}`;
  }
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
  const { t } = useTranslation();
  if (quantity <= 0) return <span className={`${styles.stock} ${styles.out}`}>{t('product.outOfStock')}</span>;
  if (quantity <= lowThreshold) return <span className={`${styles.stock} ${styles.low}`}>{t('product.stockLow', { quantity })}</span>;
  return null;
}
