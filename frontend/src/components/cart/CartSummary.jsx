import { useTranslation } from 'react-i18next';
import Button from '../common/Button';
import { formatPrice } from '../product/ProductBadges';
import styles from './CartSummary.module.css';

// ملخّص الطلب أسفل درج السلة: المجموع الفرعي، الشحن، الإجمالي، ثم زر الدفع.
export default function CartSummary({ subtotal, currency, onCheckout }) {
  const { t } = useTranslation();
  const shipping = 0;
  const total = subtotal + shipping;

  return (
    <div>
      <div className={styles.row}>
        <span>{t('cart.subtotal')}</span><span>{formatPrice(subtotal, currency)}</span>
      </div>
      <div className={styles.row}>
        <span>{t('cart.shipping')}</span><span>{shipping === 0 ? t('cart.free') : formatPrice(shipping, currency)}</span>
      </div>
      <div className={styles.total}>
        <span>{t('cart.total')}</span><span>{formatPrice(total, currency)}</span>
      </div>
      <Button variant="saffron" size="lg" className={styles.cta} onClick={onCheckout}>{t('cart.checkoutCta')}</Button>
    </div>
  );
}
