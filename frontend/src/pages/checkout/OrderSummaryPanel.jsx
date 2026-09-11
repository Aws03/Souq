import { useTranslation } from 'react-i18next';
import ProductImage from '../../components/product/ProductImage';
import { formatPrice, getProductName } from '../../components/product/ProductBadges';
import styles from './Checkout.module.css';

// عمود ملخّص الطلب (يسار الصفحة في RTL): أصناف السلة المصغّرة، ثم الفرعي والخصم (إن وُجد كوبون) والشحن (المرحلة 12 —
// "اختر طريقة" حين يلزم اختيار) والإجمالي النهائي كما سعّره الخادم.
export default function OrderSummaryPanel({ items, subtotal, discountAmount, shipping = 0, shippingPending = false, total, currency }) {
  const { t } = useTranslation();
  return (
    <aside className={styles.summary}>
      <h2 className={styles.panelTitle}>{t('checkout.orderSummaryTitle')}</h2>
      <div className={styles.items}>
        {items.map((i) => (
          <div className={styles.item} key={i.id}>
            <div className={styles.itemThumb}><ProductImage product={i} /></div>
            <div className={styles.itemInfo}>
              <div className={styles.itemName}>{getProductName(i)}</div>
              <div className={styles.itemQty}>{t('checkout.itemQty', { qty: i.qty })}</div>
            </div>
            <div className={styles.itemTotal}>{formatPrice(i.price * i.qty, i.currency)}</div>
          </div>
        ))}
      </div>

      <div className={styles.subtotalRow}><span>{t('cart.subtotal')}</span><span>{formatPrice(subtotal, currency)}</span></div>
      {discountAmount > 0 && (
        <div className={styles.discountRow}><span>{t('checkout.discount')}</span><span>-{formatPrice(discountAmount, currency)}</span></div>
      )}
      <div className={styles.subtotalRow}>
        <span>{t('cart.shipping')}</span>
        <span>{shippingPending ? t('checkout.shipping.chooseMethod') : shipping > 0 ? formatPrice(shipping, currency) : t('cart.free')}</span>
      </div>
      <div className={styles.totalRow}>
        <span>{t('cart.total')}</span><span>{formatPrice(total, currency)}</span>
      </div>
    </aside>
  );
}
