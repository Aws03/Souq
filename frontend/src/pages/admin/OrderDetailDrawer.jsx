import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api } from '../../api/client';
import { useToast } from '../../context/ToastContext';
import Drawer from '../../components/common/Drawer';
import Button from '../../components/common/Button';
import Spinner from '../../components/common/Spinner';
import { ErrorBanner } from '../../components/common/StateViews';
import { formatPrice } from '../../components/product/ProductBadges';
import { formatDateTime } from '../../i18n';
import { actorLabel } from '../../features/orders/orderView';
import ShipOrderDrawer from './ShipOrderDrawer';
import adminStyles from './Admin.module.css';
import styles from './OrderDetailDrawer.module.css';

// درج طلب في الإدارة (المرحلة 9): الأسطر والإجماليات والعنوانان وسجلّ الحالة بمن غيّرها وملاحظاتها، وإجراءات الحالة كما
// يعيدها الخادم من جدول الانتقالات (allowedActions). الشحن يفتح درج رقم التتبّع، والإلغاء يمرّ بتأكيد (يعيد المخزون).
export default function OrderDetailDrawer({ orderId, onClose, onChanged }) {
  const { t } = useTranslation();
  const toast = useToast();
  const [order, setOrder] = useState(null);
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);
  const [shipping, setShipping] = useState(false);
  const [confirmingCancel, setConfirmingCancel] = useState(false);

  const load = useCallback(() => {
    api.getOrder(orderId).then((o) => { setOrder(o); setError(null); }).catch((e) => setError(e.message));
  }, [orderId]);

  useEffect(() => { load(); }, [load]);

  const applied = () => {
    toast.success(t('admin.orders.statusUpdated'));
    setConfirmingCancel(false);
    load();
    onChanged();
  };

  const act = async (action) => {
    setBusy(true);
    try { await api.updateOrderStatus(orderId, action); applied(); }
    catch (e) { toast.error(e.message); }
    finally { setBusy(false); }
  };

  const ship = async (extra) => {
    await api.updateOrderStatus(orderId, 'Ship', extra);
    setShipping(false);
    applied();
  };

  const actions = order?.allowedActions ?? [];
  const footer = actions.length > 0 && (confirmingCancel ? (
    <div className={styles.confirm}>
      <p>{t('admin.orders.cancelConfirm')}</p>
      <div className={styles.footActions}>
        <Button variant="ghost" onClick={() => setConfirmingCancel(false)} disabled={busy}>{t('common.cancel')}</Button>
        <Button variant="danger" loading={busy} onClick={() => act('Cancel')}>{t('admin.orders.confirmCancel')}</Button>
      </div>
    </div>
  ) : (
    <div className={styles.footActions}>
      {actions.map((action) => (
        <Button key={action} variant={action === 'Cancel' ? 'danger' : 'primary'} disabled={busy}
          onClick={() => (action === 'Ship' ? setShipping(true) : action === 'Cancel' ? setConfirmingCancel(true) : act(action))}>
          {t(`admin.orders.action.${action}`)}
        </Button>
      ))}
    </div>
  ));

  return (
    <>
      <Drawer open onClose={onClose} side="right" busy={busy} width={520}
        title={order ? t('admin.orders.detailTitle', { number: order.orderNumber }) : ''} footer={footer}>
        {error && <ErrorBanner message={error} />}
        {!error && !order && <div className={styles.loading}><Spinner size={26} /></div>}

        {order && (
          <>
            <div className={styles.head}>
              <span className={styles.meta}>{formatDateTime(order.createdAt)}</span>
              <span className={`${adminStyles.statusBadge} ${adminStyles[order.status.toLowerCase()]}`}>
                {t(`admin.orders.status.${order.status}`, { defaultValue: order.status })}
              </span>
            </div>

            <h4 className={styles.sectionTitle}>{t('admin.orders.itemsTitle')}</h4>
            <ul className={styles.list}>
              {order.items.map((item) => (
                <li key={item.productId} className={styles.row}>
                  <span className={styles.grow}>{item.productName}</span>
                  <span className={styles.meta}>{formatPrice(item.unitPrice, order.currency)} × {item.quantity}</span>
                  <b>{formatPrice(item.lineTotal, order.currency)}</b>
                </li>
              ))}
            </ul>
            <div className={styles.totals}>
              {order.discountAmount > 0 && (
                <span>{t('checkout.discount')} ({order.couponCode}): -{formatPrice(order.discountAmount, order.currency)}</span>
              )}
              <b>{t('cart.total')}: {formatPrice(order.totalAmount, order.currency)}</b>
            </div>

            <h4 className={styles.sectionTitle}>{t('admin.orders.addressesTitle')}</h4>
            <dl className={styles.addresses}>
              <div><dt>{t('admin.orders.shipping')}</dt><dd>{order.shippingAddress}</dd></div>
              <div><dt>{t('admin.orders.billing')}</dt><dd>{order.billingAddress}</dd></div>
              {order.trackingNumber && (
                <div><dt>{t('orders.trackingNumber')}</dt><dd dir="ltr">{order.trackingNumber} {order.shippingCarrier ?? ''}</dd></div>
              )}
            </dl>

            <h4 className={styles.sectionTitle}>{t('admin.orders.historyTitle')}</h4>
            <ol className={styles.list}>
              {order.history.map((h, i) => (
                <li key={i} className={styles.entry}>
                  <div className={styles.row}>
                    <b className={styles.grow}>{t(`admin.orders.status.${h.status}`, { defaultValue: h.status })}</b>
                    <span className={styles.meta}>{formatDateTime(h.changedAt)}</span>
                  </div>
                  {actorLabel(h, t) && <span className={styles.meta}>{actorLabel(h, t)}</span>}
                  {h.note && <p className={styles.note}>{h.note}</p>}
                </li>
              ))}
            </ol>
          </>
        )}
      </Drawer>

      {shipping && order && (
        <ShipOrderDrawer order={{ id: order.orderNumber }} onShip={ship} onClose={() => setShipping(false)} />
      )}
    </>
  );
}
