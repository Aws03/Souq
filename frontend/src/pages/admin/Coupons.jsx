import { useState, useEffect, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { api } from '../../api/client';
import { useToast } from '../../context/ToastContext';
import DataTable from '../../components/common/DataTable';
import RowActionsMenu from '../../components/common/RowActionsMenu';
import Pagination from '../../components/common/Pagination';
import Button from '../../components/common/Button';
import { formatPrice } from '../../components/product/ProductBadges';
import { formatDate } from '../../i18n';
import CouponFormDrawer from './CouponFormDrawer';
import styles from './Admin.module.css';

const PAGE_SIZE = 20;

// شاشة إدارة الكوبونات: جدول مرقّم + درج إضافة/تعديل. الحذف فعلي (لا مرجع
// أجنبي من الطلبات — انظر تعليق DeleteCouponCommand في الخادم).
export default function Coupons() {
  const { t } = useTranslation();
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
    toast.success(editing?.id ? t('admin.coupons.updated') : t('admin.coupons.created'));
    load();
  };

  const remove = async (coupon) => {
    if (!window.confirm(t('admin.coupons.confirmDelete', { code: coupon.code }))) return;
    try {
      await api.deleteCoupon(coupon.id);
      toast.success(t('admin.coupons.deleted'));
      load();
    } catch (e) { toast.error(e.message); }
  };

  // ترتيب الأعمدة وعرضها ثابتان (colgroup في DataTable) — لا يتفاوتان حسب طول
  // المحتوى، فيبقى الجدول محاذىً بانتظام على كل صف.
  const columns = [
    { key: 'code', header: t('admin.coupons.colCode'), width: '110px', truncate: true, tooltip: (c) => c.code, render: (c) => <span dir="ltr">{c.code}</span> },
    {
      key: 'type', header: t('admin.coupons.colType'), width: '130px',
      render: (c) => (c.type === 'Percentage' ? t('admin.couponForm.typePercentage') : t('admin.couponForm.typeFixed')),
    },
    {
      key: 'value', header: t('admin.coupons.colDiscount'), width: '90px', align: 'end',
      render: (c) => (c.type === 'Percentage' ? `${c.value}%` : formatPrice(c.value, 'JOD')),
    },
    {
      key: 'minOrder', header: t('admin.coupons.colMinOrder'), width: '110px', align: 'end',
      render: (c) => (c.minOrderAmount ? formatPrice(c.minOrderAmount, 'JOD') : '—'),
    },
    { key: 'expires', header: t('admin.coupons.colExpires'), width: '110px', render: (c) => (c.expiresAt ? formatDate(c.expiresAt) : '—') },
    { key: 'uses', header: t('admin.coupons.colUsage'), width: '90px', align: 'end', render: (c) => `${c.usedCount}${c.maxUses ? ` / ${c.maxUses}` : ''}` },
    {
      key: 'status', header: t('admin.coupons.colStatus'), width: '90px',
      render: (c) => <span className={`${styles.statusBadge} ${c.isActive ? styles.paid : styles.cancelled}`}>{c.isActive ? t('admin.coupons.active') : t('admin.coupons.inactive')}</span>,
    },
    {
      key: 'actions', header: t('admin.coupons.colActions'), width: '64px', align: 'end', render: (c) => (
        <RowActionsMenu actions={[
          { label: t('common.edit'), onClick: () => setEditing(c) },
          { label: t('common.delete'), variant: 'danger', onClick: () => remove(c) },
        ]} />
      ),
    },
  ];

  return (
    <div>
      <h2 className={styles.pageTitle}>{t('admin.coupons.title')}</h2>
      <p className={styles.pageSub}>{t('admin.coupons.subtitle')}</p>

      <div className={styles.toolbar}>
        <Button variant="primary" onClick={() => setEditing({})}>{t('admin.coupons.addCoupon')}</Button>
      </div>

      <DataTable columns={columns} rows={items} rowKey={(c) => c.id} loading={loading} error={error}
        onRetry={load} emptyTitle={t('admin.coupons.emptyTitle')} emptyMessage={t('admin.coupons.emptyMessage')}
        minWidth="780px" stickyFirstColumn />

      <Pagination page={page} totalPages={totalPages} onChange={setPage} />

      {editing !== null && (
        <CouponFormDrawer coupon={editing.id ? editing : null} onSave={save} onClose={() => setEditing(null)} />
      )}
    </div>
  );
}
