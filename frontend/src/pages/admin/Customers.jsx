import { useState, useEffect, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { api } from '../../api/client';
import DataTable from '../../components/common/DataTable';
import RowActionsMenu from '../../components/common/RowActionsMenu';
import Pagination from '../../components/common/Pagination';
import { SearchIcon } from '../../components/icons/Icons';
import { formatPrice } from '../../components/product/ProductBadges';
import { formatDate } from '../../i18n';
import { CUSTOMER_STATUSES, buildCustomerQuery } from '../../features/admin/customers/customerActions';
import CustomerDetailDrawer from './CustomerDetailDrawer';
import styles from './Admin.module.css';

const PAGE_SIZE = 20;

// شاشة عملاء المتجر (المرحلة 7): بحث بالاسم/البريد/الهاتف وتصفية بالحالة، مع عدد الطلبات والإنفاق وآخر طلب، ودرج
// تفاصيل بالعناوين وآخر الطلبات وإجراءات الإيقاف والتصدير والحذف (customers.manage). الخادم يقصر القائمة على عملاء
// هذا المتجر، ومعرّف عميل متجر آخر يعيد 404.
export default function Customers() {
  const { t } = useTranslation();
  const [items, setItems] = useState([]);
  const [totalPages, setTotalPages] = useState(1);
  const [page, setPage] = useState(1);
  const [keyword, setKeyword] = useState('');
  const [status, setStatus] = useState('');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [openId, setOpenId] = useState(null);

  const load = useCallback(() => {
    setLoading(true);
    api.getCustomers(buildCustomerQuery({ keyword, status, page, pageSize: PAGE_SIZE }))
      .then((res) => { setItems(res.items); setTotalPages(res.totalPages); setError(null); })
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
  }, [keyword, status, page]);

  useEffect(() => { load(); }, [load]);
  useEffect(() => { setPage(1); }, [keyword, status]);

  const statusLabel = (c) => t(`admin.customers.status.${c.status}`, { defaultValue: c.status });

  const columns = [
    { key: 'id', header: '#', width: '56px', align: 'end', render: (c) => c.id },
    {
      key: 'customer', header: t('admin.customers.colCustomer'), width: '230px', truncate: true,
      tooltip: (c) => c.email,
      render: (c) => (
        <>
          <div>{c.fullName}</div>
          <div className={styles.nameSecondary}><bdi>{c.email}</bdi></div>
        </>
      ),
    },
    {
      key: 'status', header: t('admin.customers.colStatus'), width: '100px', truncate: true, tooltip: statusLabel,
      render: (c) => (
        <span className={`${styles.statusBadge} ${c.status === 'Blocked' ? styles.customerBlocked : styles.customerActive}`}>
          {statusLabel(c)}
        </span>
      ),
    },
    { key: 'orders', header: t('admin.customers.colOrders'), width: '80px', align: 'end', render: (c) => c.orderCount },
    { key: 'spent', header: t('admin.customers.colSpent'), width: '120px', align: 'end', render: (c) => formatPrice(c.totalSpent, c.currency) },
    { key: 'last', header: t('admin.customers.colLastOrder'), width: '110px', render: (c) => (c.lastOrderAt ? formatDate(c.lastOrderAt) : '—') },
    {
      key: 'actions', header: t('admin.customers.colActions'), width: '64px', align: 'end',
      render: (c) => <RowActionsMenu actions={[{ label: t('admin.customers.view'), onClick: () => setOpenId(c.id) }]} />,
    },
  ];

  return (
    <div>
      <h2 className={styles.pageTitle}>{t('admin.customers.title')}</h2>
      <p className={styles.pageSub}>{t('admin.customers.subtitle')}</p>

      <div className={styles.toolbar}>
        <label className={styles.search}>
          <SearchIcon size={16} />
          <input value={keyword} onChange={(e) => setKeyword(e.target.value)} placeholder={t('admin.customers.searchPlaceholder')} />
        </label>
        <select value={status} onChange={(e) => setStatus(e.target.value)} aria-label={t('admin.customers.colStatus')}>
          <option value="">{t('admin.customers.allStatuses')}</option>
          {CUSTOMER_STATUSES.map((s) => <option key={s} value={s}>{t(`admin.customers.status.${s}`)}</option>)}
        </select>
      </div>

      <DataTable columns={columns} rows={items} rowKey={(c) => c.id} loading={loading} error={error}
        onRetry={load} emptyTitle={t('admin.customers.emptyTitle')} emptyMessage={t('admin.customers.emptyMessage')}
        minWidth="760px" stickyFirstColumn />

      <Pagination page={page} totalPages={totalPages} onChange={setPage} />

      {openId && <CustomerDetailDrawer customerId={openId} onClose={() => setOpenId(null)} onChanged={load} />}
    </div>
  );
}
