import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { api } from '../api/client';
import { EmptyState, ErrorBanner } from '../components/common/StateViews';
import Skeleton from '../components/common/Skeleton';
import Pagination from '../components/common/Pagination';
import { formatPrice } from '../components/product/ProductBadges';
import { formatDate } from '../i18n';
import { ReceiptIcon, ChevronIcon } from '../components/icons/Icons';
import styles from './MyOrders.module.css';

const PAGE_SIZE = 20;

// صفحة "طلباتي": طلبات العميل الحالي (الأحدث أولاً) مرقّمة من الخادم مع شارة حالة كل
// طلب. النقر على أي طلب يفتح صفحة تتبّعه (نفس نقطة التتبّع العامة بلا مصادقة).
export default function MyOrders() {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const [orders, setOrders] = useState(null);
  const [page, setPage] = useState(1);
  const [totalPages, setTotalPages] = useState(1);
  const [error, setError] = useState(null);

  // اتجاه سهم "التالي/دخول التفاصيل" منطقي لا ثابت — نحو اتجاه القراءة الأمامي
  // (نفس منطق Pagination: يساراً في RTL، يميناً في LTR).
  const forwardDir = i18n.dir() === 'rtl' ? 'start' : 'end';

  useEffect(() => {
    api.getMyOrders({ page, pageSize: PAGE_SIZE })
      .then((res) => { setOrders(res.items); setTotalPages(res.totalPages); })
      .catch((e) => setError(e.message));
  }, [page]);

  return (
    <div className="souq-layout">
      <h1 className={styles.title}>{t('orders.myOrdersTitle')}</h1>

      {error && <ErrorBanner message={error} />}

      {!error && orders === null && (
        <div className={styles.list}>
          {Array.from({ length: 3 }).map((_, i) => <Skeleton key={i} height={78} radius={14} />)}
        </div>
      )}

      {!error && orders !== null && orders.length === 0 && (
        <EmptyState icon={ReceiptIcon} title={t('orders.emptyTitle')} message={t('orders.emptyMessage')}
          actionLabel={t('checkout.browseStore')} onAction={() => navigate('/')} />
      )}

      {!error && orders !== null && orders.length > 0 && (
        <>
          <ul className={styles.list}>
            {orders.map((o) => (
              <li key={o.id}>
                <button type="button" className={styles.row} onClick={() => navigate(`/orders/${o.id}`)}>
                  <div className={styles.rowMain}>
                    <span className={styles.orderNo}>{t('orders.orderNumber', { id: o.id })}</span>
                    <span className={styles.date}>{formatDate(o.createdAt)}</span>
                  </div>
                  <div className={styles.rowEnd}>
                    <span className={`${styles.statusBadge} ${styles[o.status.toLowerCase()]}`}>
                      {t(`orders.status.${o.status}`, { defaultValue: o.status })}
                    </span>
                    <span className={styles.total}>{formatPrice(o.totalAmount, o.currency)}</span>
                    <ChevronIcon dir={forwardDir} size={16} />
                  </div>
                </button>
              </li>
            ))}
          </ul>
          <Pagination page={page} totalPages={totalPages} onChange={setPage} />
        </>
      )}
    </div>
  );
}
