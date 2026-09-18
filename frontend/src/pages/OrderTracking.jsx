import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useQuery } from '@tanstack/react-query';
import { api } from '../api/client';
import { queryKeys } from '../app/queryKeys';
import { useToast } from '../context/ToastContext';
import { usePageMetadata } from '../app/usePageMetadata';
import Skeleton from '../components/common/Skeleton';
import { EmptyState, ErrorBanner } from '../components/common/StateViews';
import { CheckIcon, ChevronIcon, CopyIcon } from '../components/icons/Icons';
import { formatDateTime } from '../i18n';
import styles from './OrderTracking.module.css';
import StatusBadge from '../components/common/StatusBadge';
import { statusTone } from '../features/statusTone';

// ============================================================================
// صفحة التتبّع العامّة (/track/:token) — الصفحة الوحيدة بلا حارس عمداً: رابط قابل للمشاركة
// بالرمز العشوائي (128 بت) لا بالمعرّف التسلسلي، فلا يُخمَّن رابط طلب آخر (المرحلة 9، B8).
//
// تعرض ما يكشفه العقد وحده: الرقم والحالة وتواريخها ورقم الشحنة — لا اسم العميل ولا عنوانه
// ولا مبلغه ولا أسطره. الحدّ يفرضه الخادم (OrderTrackingDto)، وهذه الصفحة لا تطلب أكثر منه.
//
// رمز خاطئ أو منتهٍ ⇒ 404 من الخادم ⇒ حالة "رابط لا يعمل" مترجمة، لا رسالة خادم خام:
// الزائر هنا قد لا يكون عميل المتجر أصلاً.
// ============================================================================
export default function OrderTracking() {
  const { t } = useTranslation();
  const { token } = useParams();
  const toast = useToast();
  const [copied, setCopied] = useState(false);
  usePageMetadata({ title: t('orders.trackTitle') });


  const { data: tracking, error, isPending } = useQuery({
    queryKey: queryKeys.orderTracking(token),
    queryFn: () => api.trackOrder(token),
  });

  // 404 ليس عطلاً بل جواب: رمز خاطئ أو طلب لم يعد موجوداً — رسالة مترجمة لا لافتة خطأ.
  const notFound = error?.status === 404;

  const copyTrackingNumber = async () => {
    try {
      await navigator.clipboard.writeText(tracking.trackingNumber);
      setCopied(true);
      toast.success(t('orders.trackingCopied'));
      setTimeout(() => setCopied(false), 2000);
    } catch {
      toast.error(t('errors.connection'));
    }
  };

  return (
    <div className="souq-layout">
      <Link to="/" className={styles.backLink}>
        <ChevronIcon dir="start" size={16} /> {t('orders.backToStore')}
      </Link>

      <div className={styles.panel}>
        {error && !notFound && <ErrorBanner message={error.message} />}

        {notFound && (
          <EmptyState title={t('orders.trackNotFoundTitle')} message={t('orders.trackNotFoundMessage')} />
        )}

        {isPending && <Skeleton height={320} radius={14} />}

        {tracking && (
          <>
            <div className={styles.header}>
              <div>
                <h1 className={styles.title}>{t('orders.orderNumber', { id: tracking.orderNumber })}</h1>
                <span className={styles.stepDate}>{t('orders.placedOn', { date: formatDateTime(tracking.createdAt) })}</span>
              </div>
              <StatusBadge tone={statusTone('order', tracking.status)}>
                {t(`orders.status.${tracking.status}`, { defaultValue: tracking.status })}
              </StatusBadge>
            </div>

            {tracking.trackingNumber && (
              <div className={styles.trackingRow}>
                <div className={styles.trackingInfo}>
                  <span className={styles.trackingLabel}>{t('orders.trackingNumber')}</span>
                  <span className={styles.trackingValue}>{tracking.trackingNumber}</span>
                  {tracking.shippingCarrier && <span className={styles.carrier}>{tracking.shippingCarrier}</span>}
                  {/* رابط الناقل من قالب طريقة الشحن (المرحلة 12) — noopener لأنه وجهة خارجية. */}
                  {tracking.trackingUrl && (
                    <a className={styles.carrier} href={tracking.trackingUrl} target="_blank" rel="noopener noreferrer">
                      {t('orders.trackShipment')}
                    </a>
                  )}
                </div>
                <button type="button" className={styles.copyBtn} onClick={copyTrackingNumber}>
                  {copied ? <CheckIcon size={16} /> : <CopyIcon size={16} />}
                  {copied ? t('orders.copied') : t('orders.copy')}
                </button>
              </div>
            )}

            <h2 className={styles.stepStatus}>{t('orders.timelineTitle')}</h2>
            <ol className={styles.timeline}>
              {tracking.history.map((step, i) => (
                <li key={`${step.status}-${step.changedAt}`}
                  className={`${styles.step} ${i === tracking.history.length - 1 ? styles.stepCurrent : ''}`}>
                  <span className={styles.dot} />
                  <div className={styles.stepBody}>
                    <span className={styles.stepStatus}>
                      {t(`orders.status.${step.status}`, { defaultValue: step.status })}
                    </span>
                    <span className={styles.stepDate}>{formatDateTime(step.changedAt)}</span>
                  </div>
                </li>
              ))}
            </ol>
          </>
        )}
      </div>
    </div>
  );
}
