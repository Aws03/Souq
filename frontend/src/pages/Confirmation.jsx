import { Navigate, useLocation, useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import Button from '../components/common/Button';
import { SuccessIcon, TruckIcon } from '../components/icons/Icons';
import { formatPrice } from '../components/product/ProductBadges';
import styles from './Confirmation.module.css';

const DELIVERY_WINDOW_DAYS = [3, 5];

// شاشة تأكيد الطلب — تقرأ الطلب من حالة التوجيه. الوصول المباشر بلا طلب
// (مثل تحديث الصفحة) يعيد للمتجر بدل عرض شاشة فارغة.
export default function Confirmation() {
  const { t } = useTranslation();
  const location = useLocation();
  const navigate = useNavigate();
  const order = location.state?.order;

  if (!order) return <Navigate to="/" replace />;

  return (
    <div className="souq-layout">
      <div className={styles.panel}>
        <div className={styles.icon}><SuccessIcon size={34} /></div>
        <h2 className={styles.title}>{t('confirmation.title')}</h2>
        <p className={styles.orderNo}>{t('confirmation.orderNumber')} <b>#{order.orderId}</b></p>

        <div className={styles.delivery}>
          <TruckIcon size={18} />
          <span>{t('confirmation.deliveryWindow', { min: DELIVERY_WINDOW_DAYS[0], max: DELIVERY_WINDOW_DAYS[1] })}</span>
        </div>

        <div className={styles.totalRow}>
          <span>{t('confirmation.amountPaid')}</span><span>{formatPrice(order.total, order.currency)}</span>
        </div>

        <div className={styles.actions}>
          <Button variant="saffron" size="lg" onClick={() => navigate('/')}>{t('confirmation.continueShopping')}</Button>
          <Button variant="ghost" size="lg" onClick={() => navigate(`/orders/${order.orderId}`)}>{t('confirmation.trackOrder')}</Button>
        </div>
      </div>
    </div>
  );
}
