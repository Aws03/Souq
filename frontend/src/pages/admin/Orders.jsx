import { useState, useEffect, useCallback } from 'react';
import { api } from '../../api/client';
import Pagination from '../../components/Pagination';

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
  Delivered: [],
  Cancelled: [],
};

export default function Orders() {
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
      load();
    } catch (e) {
      setError(e.message);
    } finally {
      setBusyId(null);
    }
  };

  return (
    <div>
      <h2 className="admin-page-title">الطلبات</h2>
      <p className="admin-page-sub">تابع طلبات العملاء وحدّث حالتها.</p>

      {error && <div className="auth-alert">⚠ {error}</div>}

      <div className="admin-table-wrap">
        <table className="admin-table">
          <thead>
            <tr><th>#</th><th>العميل</th><th>الحالة</th><th>الأصناف</th><th>الإجمالي</th><th>التاريخ</th><th></th></tr>
          </thead>
          <tbody>
            {loading && <tr><td colSpan={7} className="admin-table-empty">جارٍ التحميل...</td></tr>}
            {!loading && items.length === 0 && <tr><td colSpan={7} className="admin-table-empty">لا طلبات بعد</td></tr>}
            {!loading && items.map((o) => (
              <tr key={o.id}>
                <td>{o.id}</td>
                <td>#{o.customerId}</td>
                <td><span className={`status-badge status-${o.status.toLowerCase()}`}>{STATUS_LABEL[o.status] || o.status}</span></td>
                <td>{o.itemCount}</td>
                <td>{o.totalAmount.toFixed(2)} {o.currency}</td>
                <td>{new Date(o.createdAt).toLocaleDateString('ar-SA')}</td>
                <td className="admin-table-actions">
                  {(ACTIONS[o.status] || []).map(([action, label]) => (
                    <button key={action} className="btn-ghost" disabled={busyId === o.id}
                      onClick={() => act(o, action)}>{label}</button>
                  ))}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <Pagination page={page} totalPages={totalPages} onChange={setPage} />
    </div>
  );
}
