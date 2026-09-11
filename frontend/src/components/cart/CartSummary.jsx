import { useTranslation } from 'react-i18next';
import Button from '../common/Button';
import { formatPrice } from '../product/ProductBadges';
import styles from './CartSummary.module.css';

// ملخّص السلة أسفل الدرج: المجاميع كما حسبها الخادم (الخطّ نفسه الذي يُنشئ الطلب — لا حساب هنا). الشحن (المرحلة 12):
// متجر بطرق شحن يُحسب شحنه في الدفع بعد اختيار العنوان والطريقة؛ متجر بلا طرق شحنه مجاني. blocked: سطر يمنع الدفع.
export default function CartSummary({ basket, blocked, onCheckout }) {
  const { t } = useTranslation();
  const { currency } = basket;
  const shipping = basket.shippingMethods?.required
    ? t('cart.shippingAtCheckout')
    : basket.shipping === 0 ? t('cart.free') : formatPrice(basket.shipping, currency);

  return (
    <div>
      <div className={styles.row}>
        <span>{t('cart.subtotal')}</span><span>{formatPrice(basket.subtotal, currency)}</span>
      </div>
      <div className={styles.row}>
        <span>{t('cart.shipping')}</span><span>{shipping}</span>
      </div>
      <div className={styles.total}>
        <span>{t('cart.total')}</span><span>{formatPrice(basket.total, currency)}</span>
      </div>
      {blocked && <p className={styles.blocked}>{t('cart.fixItems')}</p>}
      <Button variant="saffron" size="lg" className={styles.cta} onClick={onCheckout} disabled={blocked}>{t('cart.checkoutCta')}</Button>
    </div>
  );
}
