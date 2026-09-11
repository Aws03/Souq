import { useState, useEffect, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { api } from '../../api/client';
import DataTable from '../../components/common/DataTable';
import RowActionsMenu from '../../components/common/RowActionsMenu';
import Pagination from '../../components/common/Pagination';
import { SearchIcon } from '../../components/icons/Icons';
import { formatPrice } from '../../components/product/ProductBadges';
import { formatDate } from '../../i18n';
import { ORDER_STATUSES, buildOrderQuery } from '../../features/orders/orderView';
import OrderDetailDrawer from './OrderDetailDrawer';
import styles from './Admin.module.css';

const PAGE_SIZE = 20;

// شاشة طلبات المتجر (المرحلة 9): رقم الطلب داخل المتجر، اسم العميل، بحث (رقم الطلب أو اسم العميل أو بريده) وتصفية
// بالحالة. الإجراءات في درج الطلب كما يعيدها الخادم من جدول الانتقالات (allowedActions) — لا نسخة ثانية للقاعدة هنا.
export default function Orders() {
  const { t } = useTranslation();
  const [items, setItems] = useState([]);
  const [totalPages, setTotalPages] = useState(1);
  const [page, setPage] = useState(1);
  const [status, setStatus] = useState('');
  const [search, setSearch] = useState('');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [openId, setOpenId] = useState(null);

  const load = useCallback(() => {
    setLoading(true);
    api.getOrders(buildOrderQuery({ status, search, page, pageSize: PAGE_SIZE }))
      .then((res) => { setItems(res.items); setTotalPages(res.totalPages); setError(null); })
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
  }, [status, search, page]);

  useEffect(() => { load(); }, [load]);
  useEffect(() => { setPage(1); }, [status, search]);

  const statusLabel = (o) => t(`admin.orders.status.${o.status}`, { defaultValue: o.status });

  // عرض/محاذاة ثابتان لكل عمود (colgroup في DataTable) — لا يهتزّ الجدول بين صفحات بأطوال قيم مختلفة.
  const columns = [
    { key: 'number', header: t('admin.orders.colNumber'), width: '84px', align: 'end', render: (o) => `#${o.orderNumber}` },
    {
      key: 'customer', header: t('admin.orders.colCustomer'), width: '170px', truncate: true,
      tooltip: (o) => o.customerName ?? `#${o.customerId}`, render: (o) => o.customerName ?? `#${o.customerId}`,
    },
    {
      key: 'status', header: t('admin.orders.colStatus'), width: '130px', truncate: true, tooltip: statusLabel,
      render: (o) => <span className={`${styles.statusBadge} ${styles[o.status.toLowerCase()]}`}>{statusLabel(o)}</span>,
    },
    { key: 'count', header: t('admin.orders.colItems'), width: '80px', align: 'end', render: (o) => o.itemCount },
    { key: 'total', header: t('admin.orders.colTotal'), width: '120px', align: 'end', render: (o) => formatPrice(o.totalAmount, o.currency) },
    { key: 'date', header: t('admin.orders.colDate'), width: '110px', render: (o) => formatDate(o.createdAt) },
    {
      key: 'actions', header: t('admin.orders.colActions'), width: '64px', align: 'end',
      render: (o) => <RowActionsMenu actions={[{ label: t('admin.orders.view'), onClick: () => setOpenId(o.id) }]} />,
    },
  ];

  return (
    <div>
      <h2 className={styles.pageTitle}>{t('admin.orders.title')}</h2>
      <p className={styles.pageSub}>{t('admin.orders.subtitle')}</p>

      <div className={styles.toolbar}>
        <label className={styles.search}>
          <SearchIcon size={16} />
          <input value={search} onChange={(e) => setSearch(e.target.value)} placeholder={t('admin.orders.searchPlaceholder')} />
        </label>
        <select value={status} onChange={(e) => setStatus(e.target.value)} aria-label={t('admin.orders.colStatus')}>
          <option value="">{t('admin.orders.allStatuses')}</option>
          {ORDER_STATUSES.map((s) => <option key={s} value={s}>{t(`admin.orders.status.${s}`)}</option>)}
        </select>
      </div>

      <DataTable columns={columns} rows={items} rowKey={(o) => o.id} loading={loading} error={error}
        onRetry={load} emptyTitle={t('admin.orders.emptyTitle')} emptyMessage={t('admin.orders.emptyMessage')}
        minWidth="720px" stickyFirstColumn />

      <Pagination page={page} totalPages={totalPages} onChange={setPage} />

      {openId && <OrderDetailDrawer orderId={openId} onClose={() => setOpenId(null)} onChanged={load} />}
    </div>
  );
}
