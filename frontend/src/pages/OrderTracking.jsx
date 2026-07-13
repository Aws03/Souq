import { useEffect, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { api } from '../api/client';
import { useToast } from '../context/ToastContext';
import { ErrorBanner } from '../components/common/StateViews';
import Skeleton from '../components/common/Skeleton';
import { ChevronIcon, CopyIcon, CheckIcon } from '../components/icons/Icons';
import { formatDateTime } from '../i18n';
import styles from './OrderTracking.module.css';

// صفحة تتبّع الطلب: خط زمني عمودي لسجلّ تغييرات حالته + رقم التتبّع (مع نسخ)
// إن توفّر. تعمل بلا تسجيل دخول (رابط قابل للمشاركة، النقطة خلفها بلا مصادقة) —
// لذا لا فحص ملكية هنا؛ العقد نفسه (OrderTrackingDto) لا يكشف بيانات حسّاسة.
export default function OrderTracking() {
  const { t, i18n } = useTranslation();
  const { id } = useParams();
  const navigate = useNavigate();
  const toast = useToast();
  const [tracking, setTracking] = useState(null);
  const [error, setError] = useState(null);
  const [copied, setCopied] = useState(false);

  const backDir = i18n.dir() === 'rtl' ? 'end' : 'start';

  useEffect(() => {
    setTracking(null); setError(null);
    api.getOrderTracking(id).then(setTracking).catch((e) => setError(e.message));
  }, [id]);

  const copyTracking = async () => {
    try {
      await navigator.clipboard.writeText(tracking.trackingNumber);
      setCopied(true);
      toast.success(t('orders.trackingCopied'));
      setTimeout(() => setCopied(false), 2000);
    } catch {
      toast.error(t('errors.connection'));
    }
  };

  if (error) return <div className="souq-layout"><ErrorBanner message={error} /></div>;

  if (!tracking) {
    return (
      <div className="souq-layout">
        <Skeleton height={22} width="30%" />
        <div className={styles.panel}>
          <Skeleton height={28} width="50%" />
          <div className={styles.timeline}>
            {Array.from({ length: 3 }).map((_, i) => <Skeleton key={i} height={54} />)}
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="souq-layout">
      <button type="button" className={styles.backLink} onClick={() => navigate('/')}>
        <ChevronIcon dir={backDir} size={16} /> {t('orders.backToStore')}
      </button>

      <div className={styles.panel}>
        <div className={styles.header}>
          <h1 className={styles.title}>{t('orders.orderNumber', { id: tracking.orderId })}</h1>
          <span className={`${styles.statusBadge} ${styles[tracking.status.toLowerCase()]}`}>
            {t(`orders.status.${tracking.status}`, { defaultValue: tracking.status })}
          </span>
        </div>

        {tracking.trackingNumber && (
          <div className={styles.trackingRow}>
            <div className={styles.trackingInfo}>
              <span className={styles.trackingLabel}>{t('orders.trackingNumber')}</span>
              <span className={styles.trackingValue}>{tracking.trackingNumber}</span>
              {tracking.shippingCarrier && <span className={styles.carrier}>{tracking.shippingCarrier}</span>}
            </div>
            <button type="button" className={styles.copyBtn} onClick={copyTracking}>
              {copied ? <CheckIcon size={16} /> : <CopyIcon size={16} />}
              {copied ? t('orders.copied') : t('orders.copy')}
            </button>
          </div>
        )}

        <ol className={styles.timeline}>
          {tracking.history.map((h, i) => (
            <li key={i} className={`${styles.step} ${i === tracking.history.length - 1 ? styles.stepCurrent : ''}`}>
              <span className={styles.dot} />
              <div className={styles.stepBody}>
                <span className={styles.stepStatus}>
                  {t(`orders.status.${h.status}`, { defaultValue: h.status })}
                </span>
                <span className={styles.stepDate}>{formatDateTime(h.changedAt)}</span>
                {h.note && <p className={styles.stepNote}>{h.note}</p>}
              </div>
            </li>
          ))}
        </ol>
      </div>
    </div>
  );
}
