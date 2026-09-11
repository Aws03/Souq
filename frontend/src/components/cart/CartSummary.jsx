import { useTranslation } from 'react-i18next';
import Button from '../common/Button';
import { formatPrice } from '../product/ProductBadges';
import styles from './CartSummary.module.css';

// ملخّص السلة أسفل الدرج: المجاميع كما حسبها الخادم (الخطّ نفسه الذي يُنشئ الطلب — لا حساب هنا). الشحن صفر لا يتقاضاه
// المتجر حتى نموذج الشحن (المرحلة 12). blocked: سطر يمنع الدفع (غير متاح، يتجاوز المتاح).
export default function CartSummary({ basket, blocked, onCheckout }) {
  const { t } = useTranslation();
  const { currency } = basket;

  return (
    <div>
      <div className={styles.row}>
        <span>{t('cart.subtotal')}</span><span>{formatPrice(basket.subtotal, currency)}</span>
      </div>
      <div className={styles.row}>
        <span>{t('cart.shipping')}</span><span>{basket.shipping === 0 ? t('cart.free') : formatPrice(basket.shipping, currency)}</span>
      </div>
      <div className={styles.total}>
        <span>{t('cart.total')}</span><span>{formatPrice(basket.total, currency)}</span>
      </div>
      {blocked && <p className={styles.blocked}>{t('cart.fixItems')}</p>}
      <Button variant="saffron" size="lg" className={styles.cta} onClick={onCheckout} disabled={blocked}>{t('cart.checkoutCta')}</Button>
    </div>
  );
}
