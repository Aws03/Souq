import { useTranslation } from 'react-i18next';
import i18n from '../../i18n';
import { localizedDescription, localizedName } from '../../features/catalog/catalogText';
import { formatMoney, getStoreCurrency } from '../../app/tenantModel';
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

/** السعر بعملته وخاناتها الصغرى (Intl)، بلغة الزائر. بلا عملة صريحة ⇒ عملة المتجر من إعداده (المرحلة 15، A5). */
export function formatPrice(amount, currency = getStoreCurrency()) {
  return formatMoney(amount, currency || getStoreCurrency(), i18n.language);
}

// سعر المقارنة (قبل الخصم) يظهر مشطوباً فقط حين يعلو السعر — الخادم يضمن ذلك، والشرط هنا دفاعي.
// from (V3، P-08b): المنتج بعدّة متغيّرات بأسعار مختلفة ⇒ "ابتداءً من" أرخص ما يمكن شراؤه — الرقم من الخادم لا يُحسب هنا.
export function PriceTag({ amount, currency, compareAt, from = false }) {
  const { t } = useTranslation();
  const money = formatPrice(amount, currency);
  const price = (
    <span className={styles.price}>
      {from ? t('product.priceFrom', { price: money }) : money}
    </span>
  );
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
