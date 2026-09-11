import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api } from '../../api/client';
import { useAuth } from '../../context/AuthContext';
import { useToast } from '../../context/ToastContext';
import Drawer from '../../components/common/Drawer';
import Button from '../../components/common/Button';
import Spinner from '../../components/common/Spinner';
import { ErrorBanner } from '../../components/common/StateViews';
import { formatPrice } from '../../components/product/ProductBadges';
import { formatDate } from '../../i18n';
import { formatAddressLine } from '../../features/account/addressForm';
import { downloadJson } from '../../features/account/download';
import { customerActions, nextStatus } from '../../features/admin/customers/customerActions';
import adminStyles from './Admin.module.css';
import styles from './CustomerDetailDrawer.module.css';

const RECENT_ORDERS = 5;

// درج تفاصيل عميل: الملف والإحصاء والعناوين وآخر الطلبات (من /orders?customerId= لمن يملك orders.view)، وإجراءات
// customers.manage. الحذف يمرّ بتأكيد صريح داخل الدرج لأنه لا رجعة فيه.
export default function CustomerDetailDrawer({ customerId, onClose, onChanged }) {
  const { t } = useTranslation();
  const toast = useToast();
  const { can } = useAuth();
  const canSeeOrders = can('orders.view');
  const [customer, setCustomer] = useState(null);
  const [orders, setOrders] = useState(null);
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);
  const [confirmingErase, setConfirmingErase] = useState(false);

  const load = useCallback(() => {
    api.getCustomer(customerId).then(setCustomer).catch((e) => setError(e.message));
    if (canSeeOrders)
      api.getOrders({ customerId, page: 1, pageSize: RECENT_ORDERS })
        .then((res) => setOrders(res.items))
        .catch(() => setOrders([]));
  }, [customerId, canSeeOrders]);

  useEffect(() => { load(); }, [load]);

  const act = async (action) => {
    setBusy(true);
    try {
      if (action === 'Export') {
        downloadJson(await api.exportCustomer(customerId), `customer-${customerId}-data.json`);
        return;
      }
      if (action === 'Erase') await api.eraseCustomer(customerId);
      else await api.setCustomerStatus(customerId, nextStatus(action));
      toast.success(t(`admin.customers.done.${action}`));
      setConfirmingErase(false);
      setCustomer(await api.getCustomer(customerId));
      onChanged();
    } catch (e) { toast.error(e.message); }
    finally { setBusy(false); }
  };

  const actions = customerActions(customer, can('customers.manage'));
  const statusKey = customer?.isErased ? 'erased' : `status.${customer?.status}`;

  const footer = actions.length > 0 && (confirmingErase ? (
    <div className={styles.confirm}>
      <p>{t('admin.customers.eraseConfirm')}</p>
      <div className={styles.footActions}>
        <Button variant="ghost" onClick={() => setConfirmingErase(false)} disabled={busy}>{t('common.cancel')}</Button>
        <Button variant="danger" loading={busy} onClick={() => act('Erase')}>{t('admin.customers.eraseConfirmButton')}</Button>
      </div>
    </div>
  ) : (
    <div className={styles.footActions}>
      {actions.map((action) => (
        <Button key={action} variant={action === 'Erase' ? 'danger' : 'ghost'} disabled={busy}
          onClick={() => (action === 'Erase' ? setConfirmingErase(true) : act(action))}>
          {t(`admin.customers.action.${action}`)}
        </Button>
      ))}
    </div>
  ));

  return (
    <Drawer open onClose={onClose} side="right" busy={busy} width={480}
      title={t('admin.customers.detailTitle', { id: customerId })} footer={footer}>
      {error && <ErrorBanner message={error} />}
      {!error && !customer && <div className={styles.loading}><Spinner size={26} /></div>}

      {customer && (
        <>
          <div className={styles.head}>
            <div className={styles.identity}>
              <div className={styles.name}>{customer.fullName}</div>
              <div className={styles.meta}>
                <bdi>{customer.email}</bdi>
                {customer.phone && <> · <bdi dir="ltr">{customer.phone}</bdi></>}
              </div>
            </div>
            <span className={`${adminStyles.statusBadge} ${customer.status === 'Blocked' ? adminStyles.customerBlocked : adminStyles.customerActive}`}>
              {t(`admin.customers.${statusKey}`)}
            </span>
          </div>

          <dl className={styles.facts}>
            <div><dt>{t('admin.customers.ordersCount')}</dt><dd>{customer.orderCount}</dd></div>
            <div><dt>{t('admin.customers.totalSpent')}</dt><dd>{formatPrice(customer.totalSpent, customer.currency)}</dd></div>
            <div><dt>{t('admin.customers.joined')}</dt><dd>{formatDate(customer.createdAt)}</dd></div>
            <div><dt>{t('admin.customers.lastLogin')}</dt><dd>{customer.lastLoginAt ? formatDate(customer.lastLoginAt) : '—'}</dd></div>
          </dl>
          <p className={styles.meta}>
            {t(customer.emailConfirmed ? 'admin.customers.emailConfirmed' : 'admin.customers.emailNotConfirmed')}
            {customer.blockedAt && !customer.isErased && <> · {t('admin.customers.blockedSince', { date: formatDate(customer.blockedAt) })}</>}
          </p>

          <h4 className={styles.sectionTitle}>{t('admin.customers.addressesTitle')}</h4>
          {customer.addresses.length === 0 ? (
            <p className={styles.meta}>{t('admin.customers.noAddresses')}</p>
          ) : (
            <ul className={styles.list}>
              {customer.addresses.map((a) => (
                <li key={a.id} className={styles.item}>
                  <b>{a.label || a.recipientName}</b>
                  <span>{formatAddressLine(a)}</span>
                  <span className={styles.meta}>
                    {a.recipientName} · <bdi dir="ltr">{a.phone}</bdi>
                    {a.isDefaultShipping && ` · ${t('account.defaultShipping')}`}
                    {a.isDefaultBilling && ` · ${t('account.defaultBilling')}`}
                  </span>
                </li>
              ))}
            </ul>
          )}

          {canSeeOrders && (
            <>
              <h4 className={styles.sectionTitle}>{t('admin.customers.recentOrders')}</h4>
              {orders === null ? <Spinner size={20} /> : orders.length === 0 ? (
                <p className={styles.meta}>{t('admin.customers.noOrders')}</p>
              ) : (
                <ul className={styles.list}>
                  {orders.map((o) => (
                    <li key={o.id} className={`${styles.item} ${styles.orderRow}`}>
                      <b>#{o.id}</b>
                      <span className={`${adminStyles.statusBadge} ${adminStyles[o.status.toLowerCase()]}`}>
                        {t(`admin.orders.status.${o.status}`, { defaultValue: o.status })}
                      </span>
                      <span>{formatPrice(o.totalAmount, o.currency)}</span>
                      <span className={styles.meta}>{formatDate(o.createdAt)}</span>
                    </li>
                  ))}
                </ul>
              )}
            </>
          )}
        </>
      )}
    </Drawer>
  );
}
