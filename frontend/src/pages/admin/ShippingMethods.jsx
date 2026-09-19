import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../../api/client';
import { queryKeys } from '../../app/queryKeys';
import { useToast } from '../../context/ToastContext';
import DataTable from '../../components/common/DataTable';
import RowActionsMenu from '../../components/common/RowActionsMenu';
import Button from '../../components/common/Button';
import { useConfirmAction } from '../../components/common/useConfirmAction';
import { formatPrice } from '../../components/product/ProductBadges';
import { estimateLabel } from '../../features/checkout/shippingOptions';
import ShippingMethodFormDrawer from './ShippingMethodFormDrawer';
import styles from './Admin.module.css';
import StatusBadge from '../../components/common/StatusBadge';
import { flagTone } from '../../features/statusTone';

// طرق الشحن (المرحلة 12، store.shipping.manage): جدول + درج إضافة/تعديل. بلا طرق مفعّلة لا يتقاضى المتجر شحناً ولا يُطلب
// من العميل اختيار؛ بطرق مفعّلة يختار العميل ما يخدم دولة عنوانه. الحذف فعلي — الطلبات تحمل لقطة طريقتها.
export default function ShippingMethods() {
  const { t } = useTranslation();
  const toast = useToast();
  const confirmation = useConfirmAction();
  const queryClient = useQueryClient();
  const [editing, setEditing] = useState(null);

  // قائمة كاملة بلا ترقيم (TD-25، M10): المكسب أنّها تُقرأ من الذاكرة المؤقّتة عند العودة
  // إليها، وأنّ الإنعاش بعد تعديلٍ صار إبطالَ مفتاحٍ لا نداءً ثانياً مكتوباً بيد.
  const { data: items = [], error, isPending, refetch } = useQuery({
    queryKey: queryKeys.adminShipping({}),
    queryFn: api.getShippingMethods,
  });

  const reload = () => queryClient.invalidateQueries({ queryKey: queryKeys.adminShippingAll() });

  const save = async (payload) => {
    if (editing?.id) await api.updateShippingMethod(editing.id, payload);
    else await api.createShippingMethod(payload);
    toast.success(editing?.id ? t('admin.shipping.updated') : t('admin.shipping.created'));
    setEditing(null);
    reload();
  };

  const remove = (method) => confirmation.ask({
    title: t('admin.shipping.confirmDelete.title', { name: method.name }),
    message: t('admin.shipping.confirmDelete.message'),
    confirmLabel: t('admin.shipping.confirmDelete.action'),
    danger: true,
    action: async () => {
      await api.deleteShippingMethod(method.id);
      toast.success(t('admin.shipping.deleted'));
      reload();
    },
  });

  const columns = [
    { key: 'name', header: t('admin.shipping.colName'), width: '170px', truncate: true, tooltip: (m) => m.name, render: (m) => m.name },
    {
      key: 'price', header: t('admin.shipping.colPrice'), width: '110px', align: 'end',
      render: (m) => (m.price > 0 ? formatPrice(m.price, m.currency) : t('cart.free')),
    },
    {
      key: 'freeOver', header: t('admin.shipping.colFreeOver'), width: '120px', align: 'end',
      render: (m) => (m.freeOverAmount ? formatPrice(m.freeOverAmount, m.currency) : '—'),
    },
    { key: 'estimate', header: t('admin.shipping.colEstimate'), width: '120px', render: (m) => estimateLabel(m.minDays, m.maxDays, t) ?? '—' },
    {
      key: 'countries', header: t('admin.shipping.colCountries'), width: '130px', truncate: true,
      tooltip: (m) => m.countries.join(', '),
      render: (m) => (m.countries.length ? <span dir="ltr">{m.countries.join(', ')}</span> : t('admin.shipping.everywhere')),
    },
    {
      key: 'status', header: t('admin.shipping.colStatus'), width: '100px',
      render: (m) => (
        <StatusBadge tone={flagTone(m.isActive)}>
          {m.isActive ? t('admin.coupons.active') : t('admin.coupons.inactive')}
        </StatusBadge>
      ),
    },
    {
      key: 'actions', header: t('admin.coupons.colActions'), width: '64px', align: 'end', render: (m) => (
        <RowActionsMenu actions={[
          { label: t('common.edit'), onClick: () => setEditing(m) },
          { label: t('common.delete'), variant: 'danger', onClick: () => remove(m) },
        ]} />
      ),
    },
  ];

  return (
    <div>
      <h2 className={styles.pageTitle}>{t('admin.shipping.title')}</h2>
      <p className={styles.pageSub}>{t('admin.shipping.subtitle')}</p>

      <div className={styles.toolbar}>
        <Button variant="primary" onClick={() => setEditing({})}>{t('admin.shipping.add')}</Button>
      </div>

      <DataTable label={t('admin.shipping.title')} columns={columns} rows={items} rowKey={(m) => m.id} loading={isPending} error={error?.message}
        onRetry={refetch} emptyTitle={t('admin.shipping.emptyTitle')} emptyMessage={t('admin.shipping.emptyMessage')}
        minWidth="820px" stickyFirstColumn />

      {editing !== null && (
        <ShippingMethodFormDrawer method={editing.id ? editing : null} onSave={save} onClose={() => setEditing(null)} />
      )}
      {confirmation.dialog}
    </div>
  );
}
