import Button from '../common/Button';
import { formatPrice } from '../product/ProductBadges';
import styles from './CartSummary.module.css';

// ملخّص الطلب أسفل درج السلة: المجموع الفرعي، الشحن، الإجمالي، ثم زر الدفع.
export default function CartSummary({ subtotal, currency, onCheckout }) {
  const shipping = 0;
  const total = subtotal + shipping;

  return (
    <div>
      <div className={styles.row}>
        <span>المجموع الفرعي</span><span>{formatPrice(subtotal, currency)}</span>
      </div>
      <div className={styles.row}>
        <span>الشحن</span><span>{shipping === 0 ? 'مجاني' : formatPrice(shipping, currency)}</span>
      </div>
      <div className={styles.total}>
        <span>الإجمالي</span><span>{formatPrice(total, currency)}</span>
      </div>
      <Button variant="saffron" size="lg" className={styles.cta} onClick={onCheckout}>متابعة الدفع</Button>
    </div>
  );
}
