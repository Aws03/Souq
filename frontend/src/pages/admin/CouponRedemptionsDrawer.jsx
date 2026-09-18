import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api } from '../../api/client';
import Drawer from '../../components/common/Drawer';
import Pagination from '../../components/common/Pagination';
import Spinner from '../../components/common/Spinner';
import { ErrorBanner } from '../../components/common/StateViews';
import { formatPrice } from '../../components/product/ProductBadges';
import { formatDateTime } from '../../i18n';
import styles from './OrderDetailDrawer.module.css';
import StatusBadge from '../../components/common/StatusBadge';
import { statusTone } from '../../features/statusTone';

const PAGE_SIZE = 20;

// استخدامات كوبون (المرحلة 10): رقم الطلب والعميل والخصم وحالة الاستخدام — محجوز لطلب غير مدفوع، مؤكَّد بالدفع، أو
// محرَّر بالإلغاء (عاد للكوبون). الأحدث أولاً.
export default function CouponRedemptionsDrawer({ coupon, onClose }) {
  const { t } = useTranslation();
  const [page, setPage] = useState(1);
  const [data, setData] = useState(null);
  const [error, setError] = useState(null);

  useEffect(() => {
    api.getCouponRedemptions(coupon.id, { page, pageSize: PAGE_SIZE })
      .then((res) => { setData(res); setError(null); })
      .catch((e) => setError(e.message));
  }, [coupon.id, page]);

  return (
    <Drawer open onClose={onClose} side="right" width={480} title={t('admin.coupons.redemptionsTitle', { code: coupon.code })}>
      {error && <ErrorBanner message={error} />}
      {!error && !data && <div className={styles.loading}><Spinner size={26} /></div>}
      {data && data.items.length === 0 && <p className={styles.meta}>{t('admin.coupons.redemptionsEmpty')}</p>}
      {data && data.items.length > 0 && (
        <>
          <ul className={styles.list}>
            {data.items.map((r) => (
              <li key={r.orderId} className={styles.entry}>
                <div className={styles.row}>
                  <b className={styles.grow}>#{r.orderNumber} · {r.customerName ?? `#${r.customerId}`}</b>
                  <StatusBadge tone={statusTone('couponRedemption', r.status)}>
                    {t(`admin.coupons.redemptionStatus.${r.status}`, { defaultValue: r.status })}
                  </StatusBadge>
                </div>
                <span className={styles.meta}>-{formatPrice(r.discount, r.currency)} · {formatDateTime(r.createdAt)}</span>
              </li>
            ))}
          </ul>
          <Pagination page={page} totalPages={data.totalPages} onChange={setPage} />
        </>
      )}
    </Drawer>
  );
}
