import { Navigate, useLocation, useNavigate, useSearchParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useQuery } from '@tanstack/react-query';
import { api } from '../api/client';
import { queryKeys } from '../app/queryKeys';
import { usePageMetadata } from '../app/usePageMetadata';
import Button from '../components/common/Button';
import Skeleton from '../components/common/Skeleton';
import { SuccessIcon, TruckIcon } from '../components/icons/Icons';
import { formatPrice } from '../components/product/ProductBadges';
import { estimateLabel } from '../features/checkout/shippingOptions';
import styles from './Confirmation.module.css';

// ============================================================================
// شاشة تأكيد الطلب.
//
// كانت تَعِد المشتري بالتسليم "خلال 3–5 أيام عمل" — رقمان مكتوبان في الواجهة لا يعرفهما
// الخادم ولا المتجر. المتجر يضبط مدّة كل طريقة شحن (MinDays/MaxDays، المرحلة 12)، والطلب
// يحمل لقطتهما، فصار الوعد من الطلب نفسه؛ ومتجر لم يحدّد مدّة لا يَعِد بشيء بدلاً من أن
// نخترع له وعداً.
//
// وكانت تعيش في حالة التوجيه وحدها: تحديث الصفحة يعيد المشتري للرئيسية وكأن شيئاً لم يحدث،
// بعد أن دفع. الآن رقم الطلب في الرابط ويُقرأ من الخادم — وهو صاحب القرار: طلب غيره 404.
// حالة التوجيه تبقى للرسم الفوري بلا وميض، ولا تُصدَّق وحدها في المبلغ إن وصل تفصيل الخادم.
// ============================================================================
export default function Confirmation() {
  const { t } = useTranslation();
  const location = useLocation();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  usePageMetadata({ title: t('confirmation.title') });

  const placed = location.state?.order ?? null;
  const orderId = placed?.orderId ?? (Number(searchParams.get('order')) || null);

  // نفس مفتاح صفحة الطلب: الضغط على "تتبّع طلبك" بعدها يعرضها فوراً بلا طلب ثانٍ.
  const { data: detail, isError: unreachable, isPending } = useQuery({
    queryKey: queryKeys.order(orderId),
    queryFn: () => api.getOrder(orderId),
    enabled: orderId !== null,
  });

  if (!orderId) return <Navigate to="/" replace />;
  // رابط تأكيد لطلب ليس له (أو لم يعد موجوداً) ⇒ قائمة طلباته، لا تأكيد فارغ.
  if (unreachable && !placed) return <Navigate to="/orders" replace />;

  if (isPending && !placed) {
    return <div className="souq-layout"><Skeleton height={320} radius={14} /></div>;
  }

  const orderNumber = detail?.orderNumber ?? placed?.orderNumber ?? orderId;
  const total = detail?.totalAmount ?? placed?.total;
  const currency = detail?.currency ?? placed?.currency;
  const estimate = estimateLabel(detail?.shippingMinDays, detail?.shippingMaxDays, t);

  return (
    <div className="souq-layout">
      <div className={styles.panel}>
        <div className={styles.icon}><SuccessIcon size={34} /></div>
        <h2 className={styles.title}>{t('confirmation.title')}</h2>
        <p className={styles.orderNo}>{t('confirmation.orderNumber')} <b>#{orderNumber}</b></p>

        {/* لا صفّ تسليم أصلاً حين لا مدّة ولا طريقة — الفراغ أصدق من تقدير مخترع. */}
        {(estimate || detail?.shippingMethod) && (
          <div className={styles.delivery}>
            <TruckIcon size={18} />
            <span>{estimate ?? detail.shippingMethod}</span>
            {estimate && detail?.shippingMethod && <span className={styles.method}>{detail.shippingMethod}</span>}
          </div>
        )}

        <div className={styles.totalRow}>
          <span>{t('confirmation.amountPaid')}</span><span>{formatPrice(total, currency)}</span>
        </div>

        <div className={styles.actions}>
          <Button variant="accent" size="lg" onClick={() => navigate('/')}>{t('confirmation.continueShopping')}</Button>
          <Button variant="ghost" size="lg" onClick={() => navigate(`/orders/${orderId}`)}>{t('confirmation.trackOrder')}</Button>
        </div>
      </div>
    </div>
  );
}
