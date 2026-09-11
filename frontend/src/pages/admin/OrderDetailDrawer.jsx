import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api } from '../../api/client';
import { useAuth } from '../../context/AuthContext';
import { useToast } from '../../context/ToastContext';
import Drawer from '../../components/common/Drawer';
import Button from '../../components/common/Button';
import FormField, { inputClass } from '../../components/common/FormField';
import Spinner from '../../components/common/Spinner';
import { ErrorBanner } from '../../components/common/StateViews';
import { formatPrice } from '../../components/product/ProductBadges';
import { formatDateTime } from '../../i18n';
import { actorLabel } from '../../features/orders/orderView';
import {
  PAYMENT_STATUS_CLASS, REFUND_STATUS_CLASS, buildRefundPayload, currencyDecimals, refundProblem,
} from '../../features/admin/payments/paymentView';
import ShipOrderDrawer from './ShipOrderDrawer';
import adminStyles from './Admin.module.css';
import styles from './OrderDetailDrawer.module.css';

// درج طلب في الإدارة (المرحلة 9): الأسطر والإجماليات والعنوانان وسجلّ الحالة بمن غيّرها وملاحظاتها، وإجراءات الحالة كما
// يعيدها الخادم من جدول الانتقالات (allowedActions). الشحن يفتح درج رقم التتبّع، والإلغاء يمرّ بتأكيد (يعيد المخزون، ويردّ
// مال الطلب المدفوع). المرحلة 11: الدفعة واستردادها — نموذج استرداد حين يسمح الخادم (canRefund)، وإعادة للمعلّق.
export default function OrderDetailDrawer({ orderId, onClose, onChanged }) {
  const { t } = useTranslation();
  const toast = useToast();
  const { can } = useAuth();
  const [order, setOrder] = useState(null);
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);
  const [shipping, setShipping] = useState(false);
  const [confirmingCancel, setConfirmingCancel] = useState(false);
  const [refunding, setRefunding] = useState(false);

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

  // نتيجة الاسترداد كما أعادها الخادم: نجح، أو رفضته البوّابة، أو لم تُجب (معلّق — يُعاد).
  const refundSettled = (outcome) => {
    if (outcome.status === 'Succeeded') toast.success(t('admin.orders.payment.refundDone'));
    else if (outcome.status === 'Pending') toast.error(t('admin.orders.payment.refundPending'));
    else toast.error(t('admin.orders.payment.refundFailed', { reason: outcome.failureReason ?? '' }));
    setRefunding(false);
    load();
    onChanged();
  };

  const retry = async (refundId) => {
    setBusy(true);
    try { refundSettled(await api.retryRefund(orderId, refundId)); }
    catch (e) { toast.error(e.message); }
    finally { setBusy(false); }
  };

  const actions = order?.allowedActions ?? [];
  const footer = actions.length > 0 && (confirmingCancel ? (
    <div className={styles.confirm}>
      <p>{order.status === 'Paid' ? t('admin.orders.cancelPaidConfirm') : t('admin.orders.cancelConfirm')}</p>
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

  const payment = order?.payment;

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

            {payment && (
              <>
                <h4 className={styles.sectionTitle}>{t('admin.orders.payment.title')}</h4>
                <div className={styles.row}>
                  <span className={`${adminStyles.statusBadge} ${adminStyles[PAYMENT_STATUS_CLASS[payment.status]]}`}>
                    {t(`admin.orders.payment.status.${payment.status}`, { defaultValue: payment.status })}
                  </span>
                  <span className={styles.grow} />
                  {payment.refundedAmount > 0 && (
                    <span className={styles.meta}>
                      {t('admin.orders.payment.refunded', { amount: formatPrice(payment.refundedAmount, payment.currency) })}
                    </span>
                  )}
                </div>
                {payment.refunds.length > 0 && (
                  <ul className={styles.list}>
                    {payment.refunds.map((r) => (
                      <li key={r.id} className={styles.entry}>
                        <div className={styles.row}>
                          <b className={styles.grow}>-{formatPrice(r.amount, payment.currency)}</b>
                          <span className={`${adminStyles.statusBadge} ${adminStyles[REFUND_STATUS_CLASS[r.status]]}`}>
                            {t(`admin.orders.payment.refundStatus.${r.status}`, { defaultValue: r.status })}
                          </span>
                        </div>
                        <span className={styles.meta}>{formatDateTime(r.createdAt)}{r.reason ? ` · ${r.reason}` : ''}</span>
                        {r.failureReason && <p className={styles.note}>{r.failureReason}</p>}
                        {r.status === 'Pending' && can('store.payments.manage') && (
                          <div className={styles.footActions}>
                            <Button variant="ghost" disabled={busy} onClick={() => retry(r.id)}>{t('admin.orders.payment.retry')}</Button>
                          </div>
                        )}
                      </li>
                    ))}
                  </ul>
                )}
                {order.canRefund && (refunding
                  ? <RefundForm order={order} onSettled={refundSettled} onCancel={() => setRefunding(false)} />
                  : (
                    <div className={styles.footActions}>
                      <Button variant="ghost" disabled={busy} onClick={() => setRefunding(true)}>{t('admin.orders.payment.refund')}</Button>
                    </div>
                  ))}
              </>
            )}

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

// نموذج الاسترداد: مبلغ اختياري (فارغ ⇒ كل المتبقّي) وسبب. الخادم يحجز المبلغ ثم يسأل البوّابة — ويرفض ما يتجاوز المتاح.
function RefundForm({ order, onSettled, onCancel }) {
  const { t } = useTranslation();
  const [amount, setAmount] = useState('');
  const [reason, setReason] = useState('');
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);
  const { refundable, currency } = order.payment;
  const max = formatPrice(refundable, currency);

  const submit = async (e) => {
    e.preventDefault();
    const problem = refundProblem(amount, refundable, currencyDecimals(currency));
    if (problem) return setError(t(`admin.orders.payment.form.${problem}`, { max }));

    setBusy(true); setError(null);
    try { onSettled(await api.refundOrder(order.id, buildRefundPayload(amount, reason))); }
    catch (err) { setError(err.message); setBusy(false); }
  };

  return (
    <form className={styles.entry} onSubmit={submit}>
      {error && <ErrorBanner message={error} />}
      <FormField label={t('admin.orders.payment.form.amountLabel')} hint={t('admin.orders.payment.form.amountHint', { max })}>
        <input className={inputClass(false)} type="number" min="0" step="any" dir="ltr" value={amount}
          onChange={(e) => setAmount(e.target.value)} />
      </FormField>
      <FormField label={t('admin.orders.payment.form.reasonLabel')}>
        <input className={inputClass(false)} maxLength={500} value={reason} onChange={(e) => setReason(e.target.value)} />
      </FormField>
      <div className={styles.footActions}>
        <Button variant="ghost" type="button" onClick={onCancel} disabled={busy}>{t('common.cancel')}</Button>
        <Button variant="danger" type="submit" loading={busy}>{t('admin.orders.payment.form.submit')}</Button>
      </div>
    </form>
  );
}
