import { useState, useEffect, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { api } from '../../api/client';
import { useToast } from '../../context/ToastContext';
import DataTable from '../../components/common/DataTable';
import RowActionsMenu from '../../components/common/RowActionsMenu';
import Pagination from '../../components/common/Pagination';
import { formatPrice } from '../../components/product/ProductBadges';
import { formatDate } from '../../i18n';
import styles from './Admin.module.css';

const PAGE_SIZE = 20;

// الإجراء المتاح لكل حالة يعكس حرفياً انتقالات الكيان في Domain (Order.cs):
// Pending→Cancel فقط، Paid→Ship/Cancel، Shipped→Deliver فقط، الباقي نهائي.
const ACTIONS = {
  Pending: ['Cancel'],
  Paid: ['Ship', 'Cancel'],
  Shipped: ['Deliver'],
  Delivered: [], Cancelled: [],
};

export default function Orders() {
  const { t } = useTranslation();
  const toast = useToast();
  const [items, setItems] = useState([]);
  const [totalPages, setTotalPages] = useState(1);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [busyId, setBusyId] = useState(null);

  const load = useCallback(() => {
    setLoading(true);
    api.getOrders({ page, pageSize: PAGE_SIZE })
      .then((res) => { setItems(res.items); setTotalPages(res.totalPages); setError(null); })
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
  }, [page]);

  useEffect(() => { load(); }, [load]);

  const act = async (order, action) => {
    setBusyId(order.id);
    try {
      await api.updateOrderStatus(order.id, action);
      toast.success(t('admin.orders.statusUpdated'));
      load();
    } catch (e) { toast.error(e.message); }
    finally { setBusyId(null); }
  };

  // عرض/محاذاة ثابتان لكل عمود (colgroup في DataTable) — لا يهتزّ الجدول بين
  // صفحات بأطوال قيم مختلفة. status يقصّ بنقاط + title احتياطاً لتسميات أطول.
  const columns = [
    { key: 'id', header: '#', width: '56px', align: 'end', render: (o) => o.id },
    { key: 'customer', header: t('admin.orders.colCustomer'), width: '90px', align: 'end', render: (o) => `#${o.customerId}` },
    {
      key: 'status', header: t('admin.orders.colStatus'), width: '130px', truncate: true,
      tooltip: (o) => t(`admin.orders.status.${o.status}`, { defaultValue: o.status }),
      render: (o) => <span className={`${styles.statusBadge} ${styles[o.status.toLowerCase()]}`}>{t(`admin.orders.status.${o.status}`, { defaultValue: o.status })}</span>,
    },
    { key: 'count', header: t('admin.orders.colItems'), width: '80px', align: 'end', render: (o) => o.itemCount },
    { key: 'total', header: t('admin.orders.colTotal'), width: '120px', align: 'end', render: (o) => formatPrice(o.totalAmount, o.currency) },
    { key: 'date', header: t('admin.orders.colDate'), width: '110px', render: (o) => formatDate(o.createdAt) },
    {
      key: 'actions', header: t('admin.orders.colActions'), width: '64px', align: 'end', render: (o) => {
        const actions = ACTIONS[o.status] || [];
        if (actions.length === 0) return null;
        return (
          <RowActionsMenu disabled={busyId === o.id}
            actions={actions.map((action) => ({ label: t(`admin.orders.action.${action}`), onClick: () => act(o, action) }))} />
        );
      },
    },
  ];

  return (
    <div>
      <h2 className={styles.pageTitle}>{t('admin.orders.title')}</h2>
      <p className={styles.pageSub}>{t('admin.orders.subtitle')}</p>

      <DataTable columns={columns} rows={items} rowKey={(o) => o.id} loading={loading} error={error}
        onRetry={load} emptyTitle={t('admin.orders.emptyTitle')} emptyMessage={t('admin.orders.emptyMessage')}
        minWidth="620px" stickyFirstColumn />

      <Pagination page={page} totalPages={totalPages} onChange={setPage} />
    </div>
  );
}
