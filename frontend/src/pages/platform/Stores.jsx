import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { api } from '../../api/client';
import { queryKeys } from '../../app/queryKeys';
import DataTable from '../../components/common/DataTable';
import Pagination from '../../components/common/Pagination';
import { SearchIcon } from '../../components/icons/Icons';
import { formatDate } from '../../i18n';
import { useDebouncedValue } from '../../hooks/useDebouncedValue';
import { resumeStepFromSummary } from '../../features/platform/provisioning';
import { useProvisioningOptions } from '../../features/platform/usePlatformStore';
import { StatusBadge } from './StorePanels';
import styles from './Platform.module.css';

const PAGE_SIZE = 20;

// ============================================================================
// متاجر المنصّة (platform.tenants.manage): بحث بالاسم أو المعرّف أو النطاق، وتصفية بالحالة، ترقيم من الخادم.
//
// "صحّة" المتجر هنا جاهزيته للتسليم لا أرقام أعماله: نطاق أساسي، ومدير فعّال أو دعوة معلّقة. مبيعات متجرٍ بعينه
// لا تعبر إلى قائمة المنصّة — وهذا حدّ مقصود (نظرة المنصّة تعرض مجاميع فقط).
// متجر قيد التجهيز يُستأنف من أوّل ما ينقصه: الخطوات تُحفظ فوراً، فالمتجر نفسه حالة المعالج.
// ============================================================================
export default function Stores() {
  const { t } = useTranslation();
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState('');
  const term = useDebouncedValue(search.trim());
  const options = useProvisioningOptions();

  const params = { search: term || undefined, status: status || undefined, page, pageSize: PAGE_SIZE };
  const { data, error, isPending, refetch } = useQuery({
    queryKey: queryKeys.platformStores(params),
    queryFn: () => api.getPlatformStores(params),
    placeholderData: keepPreviousData,
  });

  const adminCell = (row) => {
    if (row.activeAdmins > 0) return t('platform.stores.admins.active', { count: row.activeAdmins });
    if (row.pendingAdminInvitations > 0) return t('platform.stores.admins.invited', { count: row.pendingAdminInvitations });
    return <span className={styles.attention}>{t('platform.stores.admins.none')}</span>;
  };

  const columns = [
    {
      key: 'name', header: t('platform.stores.colName'), width: '190px', truncate: true, tooltip: (r) => r.name,
      render: (r) => <Link to={`/platform/stores/${r.id}`} className={styles.rowLink}>{r.name}</Link>,
    },
    { key: 'slug', header: t('platform.stores.colSlug'), width: '120px', truncate: true, render: (r) => <span dir="ltr">{r.slug}</span> },
    { key: 'status', header: t('platform.stores.colStatus'), width: '120px', render: (r) => <StatusBadge status={r.status} /> },
    {
      key: 'host', header: t('platform.stores.colHost'), width: '190px', truncate: true, tooltip: (r) => r.primaryHost ?? '',
      render: (r) => (r.primaryHost ? <span dir="ltr">{r.primaryHost}</span>
        : <span className={styles.attention}>{t('platform.stores.noDomain')}</span>),
    },
    { key: 'admins', header: t('platform.stores.colAdmins'), width: '150px', render: adminCell },
    { key: 'currency', header: t('platform.stores.colCurrency'), width: '80px', render: (r) => <span dir="ltr">{r.currency}</span> },
    { key: 'created', header: t('platform.stores.colCreated'), width: '100px', render: (r) => formatDate(r.createdAt) },
    {
      key: 'action', header: t('platform.stores.colAction'), width: '130px', align: 'end',
      render: (r) => (r.status === 'Provisioning'
        ? <Link className={styles.rowLink} to={`/platform/stores/${r.id}/setup/${resumeStepFromSummary(r)}`}>{t('platform.stores.continueSetup')}</Link>
        : <Link className={styles.rowLink} to={`/platform/stores/${r.id}`}>{t('platform.stores.manage')}</Link>),
    },
  ];

  return (
    <div>
      <div className={styles.pageHead}>
        <div>
          <h1 className={styles.title}>{t('platform.stores.title')}</h1>
          <p className={styles.subtitle}>{t('platform.stores.subtitle')}</p>
        </div>
        <Link to="/platform/stores/new" className={styles.primaryLink}>{t('platform.stores.new')}</Link>
      </div>

      <div className={styles.toolbar} role="search">
        <label className={styles.search}>
          <SearchIcon size={16} />
          <span className={styles.srOnly}>{t('platform.stores.searchLabel')}</span>
          <input type="search" value={search} placeholder={t('platform.stores.searchPlaceholder')}
            onChange={(e) => { setSearch(e.target.value); setPage(1); }} />
        </label>
        <label className={styles.filter}>
          <span className={styles.srOnly}>{t('platform.stores.statusLabel')}</span>
          <select value={status} onChange={(e) => { setStatus(e.target.value); setPage(1); }}>
            <option value="">{t('platform.stores.allStatuses')}</option>
            {(options.data?.statuses ?? []).map((s) => <option key={s} value={s}>{t(`platform.status.${s}`)}</option>)}
          </select>
        </label>
      </div>

      <DataTable label={t('platform.stores.title')} columns={columns} rows={data?.items ?? []} rowKey={(r) => r.id} loading={isPending}
        error={error?.message} onRetry={refetch}
        emptyTitle={term || status ? t('platform.stores.noMatchTitle') : t('platform.stores.emptyTitle')}
        emptyMessage={term || status ? t('platform.stores.noMatchMessage') : t('platform.stores.emptyMessage')}
        minWidth="1090px" stickyFirstColumn />

      {data && <Pagination page={page} totalPages={data.totalPages} onChange={setPage} />}
    </div>
  );
}
