import { useState, useEffect, useCallback } from 'react';
import { api } from '../../api/client';
import { useToast } from '../../context/ToastContext';
import DataTable from '../../components/common/DataTable';
import RowActionsMenu from '../../components/common/RowActionsMenu';
import Pagination from '../../components/common/Pagination';
import Button from '../../components/common/Button';
import { formatPrice } from '../../components/product/ProductBadges';
import CouponFormDrawer from './CouponFormDrawer';
import styles from './Admin.module.css';

const PAGE_SIZE = 20;

// شاشة إدارة الكوبونات: جدول مرقّم + درج إضافة/تعديل. الحذف فعلي (لا مرجع
// أجنبي من الطلبات — انظر تعليق DeleteCouponCommand في الخادم).
export default function Coupons() {
  const toast = useToast();
  const [items, setItems] = useState([]);
  const [totalPages, setTotalPages] = useState(1);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [editing, setEditing] = useState(null);

  const load = useCallback(() => {
    setLoading(true);
    api.getCoupons({ page, pageSize: PAGE_SIZE })
      .then((res) => { setItems(res.items); setTotalPages(res.totalPages); setError(null); })
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
  }, [page]);

  useEffect(() => { load(); }, [load]);

  const save = async (payload) => {
    if (editing?.id) await api.updateCoupon(editing.id, payload);
    else await api.createCoupon(payload);
    setEditing(null);
    toast.success(editing?.id ? 'تم تحديث الكوبون' : 'تمت إضافة الكوبون');
    load();
  };

  const remove = async (coupon) => {
    if (!window.confirm(`حذف الكوبون "${coupon.code}"؟`)) return;
    try {
      await api.deleteCoupon(coupon.id);
      toast.success('تم حذف الكوبون');
      load();
    } catch (e) { toast.error(e.message); }
  };

  const columns = [
    { key: 'code', header: 'الرمز', render: (c) => <span dir="ltr">{c.code}</span> },
    {
      key: 'value', header: 'الخصم',
      render: (c) => (c.type === 'Percentage' ? `${c.value}%` : formatPrice(c.value, 'JOD')),
    },
    { key: 'uses', header: 'الاستخدام', render: (c) => `${c.usedCount}${c.maxUses ? ` / ${c.maxUses}` : ''}` },
    { key: 'expires', header: 'ينتهي', render: (c) => (c.expiresAt ? new Date(c.expiresAt).toLocaleDateString('ar-JO') : '—') },
    {
      key: 'status', header: 'الحالة',
      render: (c) => <span className={`${styles.statusBadge} ${c.isActive ? styles.paid : styles.cancelled}`}>{c.isActive ? 'مُفعّل' : 'معطّل'}</span>,
    },
    {
      key: 'actions', header: '', render: (c) => (
        <RowActionsMenu actions={[
          { label: 'تعديل', onClick: () => setEditing(c) },
          { label: 'حذف', variant: 'danger', onClick: () => remove(c) },
        ]} />
      ),
    },
  ];

  return (
    <div>
      <h2 className={styles.pageTitle}>إدارة الكوبونات</h2>
      <p className={styles.pageSub}>أنشئ كوبونات خصم وتابع استخدامها.</p>

      <div className={styles.toolbar}>
        <Button variant="primary" onClick={() => setEditing({})}>+ إضافة كوبون</Button>
      </div>

      <DataTable columns={columns} rows={items} rowKey={(c) => c.id} loading={loading} error={error}
        onRetry={load} emptyTitle="لا كوبونات بعد" emptyMessage="أضف أول كوبون خصم لعملائك." />

      <Pagination page={page} totalPages={totalPages} onChange={setPage} />

      {editing !== null && (
        <CouponFormDrawer coupon={editing.id ? editing : null} onSave={save} onClose={() => setEditing(null)} />
      )}
    </div>
  );
}
