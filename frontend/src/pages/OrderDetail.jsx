import { useCallback, useEffect, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { api } from '../api/client';
import { useToast } from '../context/ToastContext';
import Button from '../components/common/Button';
import Skeleton from '../components/common/Skeleton';
import { ErrorBanner } from '../components/common/StateViews';
import { ChevronIcon, CopyIcon, CheckIcon } from '../components/icons/Icons';
import { formatPrice } from '../components/product/ProductBadges';
import { formatDateTime } from '../i18n';
import { trackingUrl } from '../features/orders/orderView';
import styles from './OrderDetail.module.css';

// صفحة طلب العميل (المرحلة 9، محمية): رقم الطلب، أسطره وإجمالياته كما ثُبّتت، العنوانان، رقم تتبّع الشحنة، الخط الزمني،
// رابط التتبّع العام لمشاركته (بالرمز العشوائي لا بالمعرّف)، والإلغاء قبل الدفع. الخادم يقرّر الملكية (404 لغير صاحبه).
export default function OrderDetail() {
  const { t, i18n } = useTranslation();
  const { id } = useParams();
  const navigate = useNavigate();
  const toast = useToast();
  const [order, setOrder] = useState(null);
  const [error, setError] = useState(null);
  const [copied, setCopied] = useState(false);
  const [confirmingCancel, setConfirmingCancel] = useState(false);
  const [cancelling, setCancelling] = useState(false);

  const backDir = i18n.dir() === 'rtl' ? 'end' : 'start';

  const load = useCallback(() => {
    api.getOrder(id).then((o) => { setOrder(o); setError(null); }).catch((e) => setError(e.message));
  }, [id]);

  useEffect(() => { setOrder(null); load(); }, [load]);

  const copyLink = async () => {
    try {
      await navigator.clipboard.writeText(trackingUrl(window.location.origin, order.trackingToken));
      setCopied(true);
      toast.success(t('orders.linkCopied'));
      setTimeout(() => setCopied(false), 2000);
    } catch {
      toast.error(t('errors.connection'));
    }
  };

  const cancel = async () => {
    setCancelling(true);
    try {
      await api.cancelMyOrder(order.id);
      toast.success(t('orders.cancelledToast'));
      setConfirmingCancel(false);
      load();
    } catch (e) { toast.error(e.message); }
    finally { setCancelling(false); }
  };

  if (error) return <div className="souq-layout"><ErrorBanner message={error} /></div>;
  if (!order) return <div className="souq-layout"><Skeleton height={420} radius={14} /></div>;

  return (
    <div className="souq-layout">
      <button type="button" className={styles.backLink} onClick={() => navigate('/orders')}>
        <ChevronIcon dir={backDir} size={16} /> {t('orders.backToOrders')}
      </button>

      <div className={styles.grid}>
        <section className={styles.panel}>
          <div className={styles.header}>
            <div>
              <h1 className={styles.title}>{t('orders.orderNumber', { id: order.orderNumber })}</h1>
              <span className={styles.meta}>{t('orders.placedOn', { date: formatDateTime(order.createdAt) })}</span>
            </div>
            <span className={`${styles.statusBadge} ${styles[order.status.toLowerCase()]}`}>
              {t(`orders.status.${order.status}`, { defaultValue: order.status })}
            </span>
          </div>

          <ul className={styles.items}>
            {order.items.map((item) => (
              <li key={item.productId} className={styles.item}>
                <span className={styles.itemName}>{item.productName}</span>
                <span className={styles.itemQty}>× {item.quantity}</span>
                <span className={styles.itemTotal}>{formatPrice(item.lineTotal, order.currency)}</span>
              </li>
            ))}
          </ul>

          <dl className={styles.totals}>
            <div><dt>{t('cart.subtotal')}</dt><dd>{formatPrice(order.subtotal, order.currency)}</dd></div>
            {order.discountAmount > 0 && (
              <div className={styles.discount}>
                <dt>{t('checkout.discount')}{order.couponCode ? ` (${order.couponCode})` : ''}</dt>
                <dd>-{formatPrice(order.discountAmount, order.currency)}</dd>
              </div>
            )}
            <div className={styles.grandTotal}><dt>{t('cart.total')}</dt><dd>{formatPrice(order.totalAmount, order.currency)}</dd></div>
            {/* ما رُدّ للعميل من دفعته (المرحلة 11) — المبلغ وحده، بلا أسباب الإدارة. */}
            {order.payment?.refundedAmount > 0 && (
              <div className={styles.discount}>
                <dt>{t('orders.refundedLabel')}</dt>
                <dd>-{formatPrice(order.payment.refundedAmount, order.payment.currency)}</dd>
              </div>
            )}
          </dl>

          <div className={styles.addresses}>
            <div><h3>{t('orders.shippingAddress')}</h3><p>{order.shippingAddress}</p></div>
            <div><h3>{t('orders.billingAddress')}</h3><p>{order.billingAddress}</p></div>
          </div>

          {order.canCancel && (confirmingCancel ? (
            <div className={styles.confirmBox}>
              <p>{t('orders.cancelConfirm')}</p>
              <div className={styles.actions}>
                <Button variant="ghost" onClick={() => setConfirmingCancel(false)} disabled={cancelling}>{t('orders.keepOrder')}</Button>
                <Button variant="danger" loading={cancelling} onClick={cancel}>{t('orders.cancelOrder')}</Button>
              </div>
            </div>
          ) : (
            <Button variant="ghost" onClick={() => setConfirmingCancel(true)}>{t('orders.cancelOrder')}</Button>
          ))}
        </section>

        <aside className={styles.panel}>
          {order.trackingNumber && (
            <div className={styles.trackingRow}>
              <span className={styles.meta}>{t('orders.trackingNumber')}</span>
              <span className={styles.trackingValue}>{order.trackingNumber}</span>
              {order.shippingCarrier && <span className={styles.meta}>{order.shippingCarrier}</span>}
            </div>
          )}

          <h2 className={styles.sectionTitle}>{t('orders.timelineTitle')}</h2>
          <ol className={styles.timeline}>
            {order.history.map((h, i) => (
              <li key={i} className={`${styles.step} ${i === order.history.length - 1 ? styles.stepCurrent : ''}`}>
                <span className={styles.dot} />
                <div className={styles.stepBody}>
                  <span className={styles.stepStatus}>{t(`orders.status.${h.status}`, { defaultValue: h.status })}</span>
                  <span className={styles.meta}>{formatDateTime(h.changedAt)}</span>
                </div>
              </li>
            ))}
          </ol>

          <div className={styles.share}>
            <h3>{t('orders.trackingLinkTitle')}</h3>
            <p className={styles.meta}>{t('orders.trackingLinkHint')}</p>
            <button type="button" className={styles.copyBtn} onClick={copyLink}>
              {copied ? <CheckIcon size={16} /> : <CopyIcon size={16} />}
              {copied ? t('orders.copied') : t('orders.copyLink')}
            </button>
          </div>
        </aside>
      </div>
    </div>
  );
}
