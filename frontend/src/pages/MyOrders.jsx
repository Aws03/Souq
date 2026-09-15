import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { api } from '../api/client';
import { queryKeys } from '../app/queryKeys';
import { usePageMetadata } from '../app/usePageMetadata';
import Skeleton from '../components/common/Skeleton';
import Pagination from '../components/common/Pagination';
import { EmptyState, ErrorBanner } from '../components/common/StateViews';
import { ReceiptIcon } from '../components/icons/Icons';
import { formatPrice } from '../components/product/ProductBadges';
import { formatDateTime } from '../i18n';
import styles from './MyOrders.module.css';

const PAGE_SIZE = 10;

// ============================================================================
// "طلباتي" — سجلّ طلبات العميل الحالي، مرقّماً، الأحدث أوّلاً (GET /api/orders/mine).
// الخادم يستخرج العميل من الجلسة؛ لا معرّف عميل يُرسَل من المتصفّح، وطلب غيره لا يظهر هنا ولا يُفتح.
//
// الصفحة في الرابط لا في الحالة (?page=2): سهم الرجوع يعود إلى الصفحة نفسها بعد فتح طلب،
// والرابط قابل للمشاركة والتحديث — نفس قاعدة الكتالوج في هذه المرحلة.
//
// كل سطر رابط <Link> لا زر: يُفتح في تبويب جديد، وتراه محرّكات القراءة رابطاً.
// ============================================================================
export default function MyOrders() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();
  usePageMetadata({ title: t('orders.myOrdersTitle') });

  const page = Math.max(1, Number(searchParams.get('page')) || 1);

  // طبقة الاستعلام (ADR-0037) تتكفّل بالإلغاء وبإبقاء الصفحة السابقة معروضة أثناء جلب التالية.
  const { data: result, error, refetch, isPending } = useQuery({
    queryKey: queryKeys.myOrders(page, PAGE_SIZE),
    queryFn: () => api.getMyOrders({ page, pageSize: PAGE_SIZE }),
    placeholderData: keepPreviousData,
  });

  const goToPage = (next) => {
    const params = new URLSearchParams(searchParams);
    if (next <= 1) params.delete('page'); else params.set('page', String(next));
    setSearchParams(params);
  };

  return (
    <section>
      <h2 className={styles.title}>{t('orders.myOrdersTitle')}</h2>

      {error && <ErrorBanner message={error.message} onRetry={refetch} />}

      {!error && isPending && (
        <div className={styles.list}>
          {Array.from({ length: 3 }, (_, i) => <Skeleton key={i} height={84} radius={14} />)}
        </div>
      )}

      {result && result.items.length === 0 && (
        <EmptyState
          icon={ReceiptIcon}
          title={t('orders.emptyTitle')}
          message={t('orders.emptyMessage')}
          actionLabel={t('orders.backToStore')}
          onAction={() => navigate('/')}
        />
      )}

      {result && result.items.length > 0 && (
        <>
          <ul className={styles.list}>
            {result.items.map((order) => (
              <li key={order.id}>
                <Link to={`/orders/${order.id}`} className={styles.row}>
                  <span className={styles.rowMain}>
                    <span className={styles.orderNo}>{t('orders.orderNumber', { id: order.orderNumber })}</span>
                    <span className={styles.date}>{formatDateTime(order.createdAt)}</span>
                  </span>
                  <span className={styles.rowEnd}>
                    <span className={styles.total}>{formatPrice(order.totalAmount, order.currency)}</span>
                    <span className={`${styles.statusBadge} ${styles[order.status.toLowerCase()]}`}>
                      {t(`orders.status.${order.status}`, { defaultValue: order.status })}
                    </span>
                  </span>
                </Link>
              </li>
            ))}
          </ul>
          <Pagination page={result.pageNumber} totalPages={result.totalPages} onChange={goToPage} />
        </>
      )}
    </section>
  );
}
