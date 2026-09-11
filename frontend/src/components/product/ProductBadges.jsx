import { useTranslation } from 'react-i18next';
import i18n from '../../i18n';
import { localizedDescription, localizedName } from '../../features/catalog/catalogText';
import styles from './ProductBadges.module.css';

/**
 * اسم المنتج (أو الفئة) بلغة الواجهة الحالية من translations، وإلا نص لغة المتجر الافتراضية
 * (name) — ويقرأ عناصر سلة/مفضّلة قديمة محفوظة في localStorage بشكل nameAr/nameEn.
 */
export function getProductName(product) {
  return localizedName(product, i18n.language);
}

export function getProductDescription(product) {
  return localizedDescription(product, i18n.language);
}

// الفئات بالشكل نفسه (name + translations).
export const getCategoryName = getProductName;

/** يهيّئ السعر بصيغة الدينار الأردني (٣ خانات عشرية: فلس)، مثال: 59.900 د.أ / 59.900 JOD */
export function formatPrice(amount, currency = 'JOD') {
  if (currency === 'JOD') {
    const symbol = i18n.language === 'ar' ? 'د.أ' : 'JOD';
    return `${Number(amount).toFixed(3)} ${symbol}`;
  }
  return `${Number(amount).toFixed(2)} ${currency}`;
}

// سعر المقارنة (قبل الخصم) يظهر مشطوباً فقط حين يعلو السعر — الخادم يضمن ذلك، والشرط هنا دفاعي.
export function PriceTag({ amount, currency, compareAt }) {
  const price = <span className={styles.price}>{formatPrice(amount, currency)}</span>;
  if (compareAt == null || Number(compareAt) <= Number(amount)) return price;
  return (
    <span className={styles.onSale}>
      {price}
      <s className={styles.compareAt}>{formatPrice(compareAt, currency)}</s>
    </span>
  );
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
