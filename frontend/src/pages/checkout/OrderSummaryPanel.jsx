import ProductImage from '../../components/product/ProductImage';
import { formatPrice } from '../../components/product/ProductBadges';
import styles from './Checkout.module.css';

// عمود ملخّص الطلب (يسار الصفحة في RTL): أصناف السلة المصغّرة ثم الإجمالي.
export default function OrderSummaryPanel({ items, total, currency }) {
  return (
    <aside className={styles.summary}>
      <h2 className={styles.panelTitle}>ملخّص الطلب</h2>
      <div className={styles.items}>
        {items.map((i) => (
          <div className={styles.item} key={i.id}>
            <div className={styles.itemThumb}><ProductImage product={i} /></div>
            <div className={styles.itemInfo}>
              <div className={styles.itemName}>{i.name}</div>
              <div className={styles.itemQty}>الكمية: {i.qty}</div>
            </div>
            <div className={styles.itemTotal}>{formatPrice(i.price * i.qty, i.currency)}</div>
          </div>
        ))}
      </div>
      <div className={styles.totalRow}>
        <span>الإجمالي</span><span>{formatPrice(total, currency)}</span>
      </div>
    </aside>
  );
}
