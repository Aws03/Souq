import { Navigate, useLocation, useNavigate } from 'react-router-dom';
import Button from '../components/common/Button';
import { SuccessIcon, TruckIcon } from '../components/icons/Icons';
import { formatPrice } from '../components/product/ProductBadges';
import styles from './Confirmation.module.css';

const DELIVERY_WINDOW_DAYS = [3, 5];

// شاشة تأكيد الطلب — تقرأ الطلب من حالة التوجيه. الوصول المباشر بلا طلب
// (مثل تحديث الصفحة) يعيد للمتجر بدل عرض شاشة فارغة.
export default function Confirmation() {
  const location = useLocation();
  const navigate = useNavigate();
  const order = location.state?.order;

  if (!order) return <Navigate to="/" replace />;

  return (
    <div className="souq-layout">
      <div className={styles.panel}>
        <div className={styles.icon}><SuccessIcon size={34} /></div>
        <h2 className={styles.title}>تم استلام طلبك بنجاح</h2>
        <p className={styles.orderNo}>رقم الطلب <b>#{order.orderId}</b></p>

        <div className={styles.delivery}>
          <TruckIcon size={18} />
          <span>التوصيل المتوقّع خلال {DELIVERY_WINDOW_DAYS[0]}–{DELIVERY_WINDOW_DAYS[1]} أيام عمل</span>
        </div>

        <div className={styles.totalRow}>
          <span>المبلغ المدفوع</span><span>{formatPrice(order.total, order.currency)}</span>
        </div>

        <div className={styles.actions}>
          <Button variant="saffron" size="lg" onClick={() => navigate('/')}>متابعة التسوّق</Button>
          <Button variant="ghost" size="lg" onClick={() => navigate('/')}>تتبّع طلبك</Button>
        </div>
      </div>
    </div>
  );
}
