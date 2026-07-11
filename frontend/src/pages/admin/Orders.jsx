import { useState, useEffect, useCallback } from 'react';
import { api } from '../../api/client';
import { useToast } from '../../context/ToastContext';
import DataTable from '../../components/common/DataTable';
import RowActionsMenu from '../../components/common/RowActionsMenu';
import Pagination from '../../components/common/Pagination';
import { formatPrice } from '../../components/product/ProductBadges';
import styles from './Admin.module.css';

const PAGE_SIZE = 20;

const STATUS_LABEL = {
  Pending: 'بانتظار الدفع', Paid: 'مدفوع', Shipped: 'تم الشحن',
  Delivered: 'تم التسليم', Cancelled: 'ملغى',
};

// الإجراء المتاح لكل حالة يعكس حرفياً انتقالات الكيان في Domain (Order.cs):
// Pending→Cancel فقط، Paid→Ship/Cancel، Shipped→Deliver فقط، الباقي نهائي.
const ACTIONS = {
  Pending: [['Cancel', 'إلغاء']],
  Paid: [['Ship', 'شحن'], ['Cancel', 'إلغاء']],
  Shipped: [['Deliver', 'تسليم']],
  Delivered: [], Cancelled: [],
};

export default function Orders() {
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
      toast.success('تم تحديث حالة الطلب');
      load();
    } catch (e) { toast.error(e.message); }
    finally { setBusyId(null); }
  };

  const columns = [
    { key: 'id', header: '#', render: (o) => o.id },
    { key: 'customer', header: 'العميل', render: (o) => `#${o.customerId}` },
    {
      key: 'status', header: 'الحالة',
      render: (o) => <span className={`${styles.statusBadge} ${styles[o.status.toLowerCase()]}`}>{STATUS_LABEL[o.status] || o.status}</span>,
    },
    { key: 'count', header: 'الأصناف', render: (o) => o.itemCount },
    { key: 'total', header: 'الإجمالي', render: (o) => formatPrice(o.totalAmount, o.currency) },
    { key: 'date', header: 'التاريخ', render: (o) => new Date(o.createdAt).toLocaleDateString('ar-JO') },
    {
      key: 'actions', header: '', render: (o) => {
        const actions = ACTIONS[o.status] || [];
        if (actions.length === 0) return null;
        return (
          <RowActionsMenu disabled={busyId === o.id}
            actions={actions.map(([action, label]) => ({ label, onClick: () => act(o, action) }))} />
        );
      },
    },
  ];

  return (
    <div>
      <h2 className={styles.pageTitle}>الطلبات</h2>
      <p className={styles.pageSub}>تابع طلبات العملاء وحدّث حالتها.</p>

      <DataTable columns={columns} rows={items} rowKey={(o) => o.id} loading={loading} error={error}
        onRetry={load} emptyTitle="لا طلبات بعد" emptyMessage="ستظهر طلبات العملاء هنا فور ورودها." />

      <Pagination page={page} totalPages={totalPages} onChange={setPage} />
    </div>
  );
}
