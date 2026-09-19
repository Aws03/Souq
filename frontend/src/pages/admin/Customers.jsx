import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../../api/client';
import { queryKeys } from '../../app/queryKeys';
import { useDebouncedValue } from '../../hooks/useDebouncedValue';
import DataTable from '../../components/common/DataTable';
import RowActionsMenu from '../../components/common/RowActionsMenu';
import Pagination from '../../components/common/Pagination';
import { SearchIcon } from '../../components/icons/Icons';
import { formatPrice } from '../../components/product/ProductBadges';
import { formatDate } from '../../i18n';
import { CUSTOMER_STATUSES, buildCustomerQuery } from '../../features/admin/customers/customerActions';
import CustomerDetailDrawer from './CustomerDetailDrawer';
import styles from './Admin.module.css';
import StatusBadge from '../../components/common/StatusBadge';
import { statusTone } from '../../features/statusTone';

const PAGE_SIZE = 20;

// شاشة عملاء المتجر (المرحلة 7): بحث بالاسم/البريد/الهاتف وتصفية بالحالة، مع عدد الطلبات والإنفاق وآخر طلب، ودرج
// تفاصيل بالعناوين وآخر الطلبات وإجراءات الإيقاف والتصدير والحذف (customers.manage). الخادم يقصر القائمة على عملاء
// هذا المتجر، ومعرّف عميل متجر آخر يعيد 404.
export default function Customers() {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const [page, setPage] = useState(1);
  const [keyword, setKeyword] = useState('');
  const [status, setStatus] = useState('');
  const [openId, setOpenId] = useState(null);
  const term = useDebouncedValue(keyword.trim());

  // المفتاح يحمل معايير العرض كلّها (TD-25، M10): ردٌّ لبحثٍ تجاوزه المستخدم يُكتب في مفتاحه لا على الشاشة.
  const params = buildCustomerQuery({ keyword: term, status, page, pageSize: PAGE_SIZE });
  const { data, error, isPending, refetch } = useQuery({
    queryKey: queryKeys.adminCustomers(params),
    queryFn: () => api.getCustomers(params),
    placeholderData: keepPreviousData,
  });

  const reload = () => queryClient.invalidateQueries({ queryKey: queryKeys.adminCustomersAll() });
  // في المعالِج لا في تأثير: التصفية سببها ضغطة المستخدم.
  const filterBy = (setter) => (value) => { setter(value); setPage(1); };

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
        <StatusBadge tone={statusTone('customer', c.status)}>
          {statusLabel(c)}
        </StatusBadge>
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
          <input value={keyword} onChange={(e) => filterBy(setKeyword)(e.target.value)} placeholder={t('admin.customers.searchPlaceholder')} />
        </label>
        <select value={status} onChange={(e) => filterBy(setStatus)(e.target.value)} aria-label={t('admin.customers.colStatus')}>
          <option value="">{t('admin.customers.allStatuses')}</option>
          {CUSTOMER_STATUSES.map((s) => <option key={s} value={s}>{t(`admin.customers.status.${s}`)}</option>)}
        </select>
      </div>

      <DataTable label={t('admin.customers.title')} columns={columns} rows={data?.items ?? []} rowKey={(c) => c.id} loading={isPending} error={error?.message}
        onRetry={refetch} emptyTitle={t('admin.customers.emptyTitle')} emptyMessage={t('admin.customers.emptyMessage')}
        minWidth="760px" stickyFirstColumn />

      {data && <Pagination page={page} totalPages={data.totalPages} onChange={setPage} />}

      {openId && <CustomerDetailDrawer customerId={openId} onClose={() => setOpenId(null)} onChanged={reload} />}
    </div>
  );
}
