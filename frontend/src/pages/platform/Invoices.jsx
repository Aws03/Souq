import { useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { api } from '../../api/client';
import { queryKeys } from '../../app/queryKeys';
import DataTable from '../../components/common/DataTable';
import Pagination from '../../components/common/Pagination';
import StatusBadge from '../../components/common/StatusBadge';
import { SearchIcon } from '../../components/icons/Icons';
import { formatPrice } from '../../components/product/ProductBadges';
import { statusTone } from '../../features/statusTone';
import { formatDate } from '../../i18n';
import {
  INVOICE_STATUSES, displayStatus, filtersFromSearch, invoiceQuery, pageFromSearch, searchFromFilters,
} from '../../features/platform/invoices';
import styles from './Platform.module.css';

const PAGE_SIZE = 20;

// ============================================================================
// فواتيرُ اشتراكات التجّار (platform.billing.manage — المالك وحده). C5، ADR-0056.
//
// **هذه الشاشة هي دفترُ سوق**: ما صدر على كلّ تاجر، وما بقي عليه، وما تأخّر. وهي المكانُ الذي
// يصير فيه قرار المالك `D-13` = A عملاً يوميّاً — فلا عمولة تأتي من مال متسوّق، والاشتراكُ
// المُفوتَر هو كلُّ إيراد المنصّة.
//
// **ولا مجموعَ عبر المتاجر في أيّ عمود.** كلُّ فاتورة بعملتها كما صدرت، والجمعُ عبر عملات مختلفة
// يُنتج رقماً لا يعني شيئاً — وهو القيدُ نفسه الذي تعلنه نظرةُ المنصّة عن إيراد المتاجر.
//
// والمرشّحاتُ في العنوان لا في الذاكرة (نمط `Audit.jsx`): زرُّ الرجوع يُعيد ما كان يُقرأ، ورابطٌ
// يُنسَخ لزميل يصل إلى الشيء نفسه.
// ============================================================================
export default function Invoices() {
  const { t } = useTranslation();
  const [search, setSearch] = useSearchParams();
  const filters = filtersFromSearch(search);
  const page = pageFromSearch(search);
  const [term, setTerm] = useState(filters.q);

  const params = invoiceQuery(filters, page, PAGE_SIZE);
  const { data, error, isPending, refetch } = useQuery({
    queryKey: queryKeys.platformInvoices(params),
    queryFn: () => api.getPlatformInvoices(params),
    placeholderData: keepPreviousData,
  });

  const settings = useQuery({
    queryKey: queryKeys.platformBillingSettings(),
    queryFn: api.getPlatformBillingSettings,
  });

  const apply = (next, nextPage = 1) => setSearch(searchFromFilters({ ...filters, ...next }, nextPage));

  const columns = [
    {
      key: 'number',
      header: t('platform.invoices.colNumber'),
      width: '150px',
      truncate: true,
      render: (row) => (
        <Link to={`/platform/invoices/${row.id}`} className={styles.rowLink}>
          {/* رقمُ المستند لاتينيٌّ دائماً، فيُعزَل اتجاهُه داخل واجهةٍ عربية. والمسوّدةُ بلا رقم
              تُقرأ باسمها لا بفراغ — وهي قابلةٌ للفتح تماماً كغيرها. */}
          {row.number ? <span dir="ltr">{row.number}</span> : t('platform.invoices.unnumbered')}
        </Link>
      ),
    },
    {
      key: 'store',
      header: t('platform.invoices.colStore'),
      width: '190px',
      truncate: true,
      tooltip: (row) => row.tenantName ?? '',
      render: (row) => row.tenantName ?? t('platform.invoices.unknownStore'),
    },
    {
      key: 'status',
      header: t('platform.invoices.colStatus'),
      width: '130px',
      render: (row) => (
        <StatusBadge tone={statusTone('invoice', displayStatus(row))}>
          {t(`platform.invoices.status.${displayStatus(row)}`)}
        </StatusBadge>
      ),
    },
    {
      key: 'total',
      header: t('platform.invoices.colTotal'),
      width: '130px',
      align: 'end',
      // بعملة الصفّ صراحةً: مضيفُ المنصّة بلا عملةِ متجر، والمجاميعُ لا تُجمَع عبر العملات.
      render: (row) => formatPrice(row.total, row.currency),
    },
    {
      key: 'outstanding',
      header: t('platform.invoices.colOutstanding'),
      width: '130px',
      align: 'end',
      render: (row) => (row.outstanding > 0
        ? <span className={styles.attention}>{formatPrice(row.outstanding, row.currency)}</span>
        : formatPrice(0, row.currency)),
    },
    {
      key: 'issued',
      header: t('platform.invoices.colIssued'),
      width: '110px',
      render: (row) => (row.issuedAtUtc ? formatDate(row.issuedAtUtc) : '—'),
    },
    {
      key: 'due',
      header: t('platform.invoices.colDue'),
      width: '150px',
      render: (row) => {
        if (!row.dueAtUtc) return '—';
        return row.isOverdue
          ? <span className={styles.attention}>{t('platform.invoices.overdueBy', { count: row.daysOverdue })}</span>
          : formatDate(row.dueAtUtc);
      },
    },
  ];

  const filtered = Boolean(filters.status || filters.tenantId || filters.overdueOnly || filters.q);

  return (
    <div>
      <div className={styles.pageHead}>
        <div>
          <h1 className={styles.title}>{t('platform.invoices.title')}</h1>
          <p className={styles.subtitle}>{t('platform.invoices.subtitle')}</p>
        </div>
        <Link to="/platform/billing" className={styles.secondaryLink}>{t('platform.invoices.settingsLink')}</Link>
      </div>

      {/* الإعدادُ ناقصٌ ⇒ لا تُصدَر فاتورة. يُقال هنا وبسببه، لا بزرٍّ معطَّل بلا تفسير — وهو
          الشكل الذي يدخل به قرارُ المالك `C-15` إلى الشاشة. */}
      {settings.data && !settings.data.canIssue && (
        <p className={styles.notice} role="status">
          {t(`platform.billing.blocked.${settings.data.blockingReason ?? 'BillingSettingsMissing'}`)}
          {' '}
          <Link to="/platform/billing" className={styles.rowLink}>{t('platform.billing.configure')}</Link>
        </p>
      )}

      <div className={styles.toolbar} role="search">
        <label className={styles.search}>
          <SearchIcon size={16} />
          <span className={styles.srOnly}>{t('platform.invoices.searchLabel')}</span>
          <input
            type="search"
            value={term}
            placeholder={t('platform.invoices.searchPlaceholder')}
            onChange={(e) => setTerm(e.target.value)}
            onKeyDown={(e) => { if (e.key === 'Enter') apply({ q: term.trim() }); }}
            onBlur={() => { if (term.trim() !== filters.q) apply({ q: term.trim() }); }}
          />
        </label>

        <label className={styles.filter}>
          <span className={styles.srOnly}>{t('platform.invoices.statusLabel')}</span>
          <select value={filters.status} onChange={(e) => apply({ status: e.target.value })}>
            <option value="">{t('platform.invoices.allStatuses')}</option>
            {INVOICE_STATUSES.map((s) => (
              <option key={s} value={s}>{t(`platform.invoices.status.${s}`)}</option>
            ))}
          </select>
        </label>

        <label className={styles.option}>
          <input
            type="checkbox"
            checked={filters.overdueOnly}
            onChange={(e) => apply({ overdueOnly: e.target.checked })}
          />
          {t('platform.invoices.overdueOnly')}
        </label>
      </div>

      <DataTable
        label={t('platform.invoices.title')}
        columns={columns}
        rows={data?.items ?? []}
        rowKey={(row) => row.id}
        loading={isPending}
        error={error?.message}
        onRetry={refetch}
        emptyTitle={filtered ? t('platform.invoices.noMatchTitle') : t('platform.invoices.emptyTitle')}
        emptyMessage={filtered ? t('platform.invoices.noMatchMessage') : t('platform.invoices.emptyMessage')}
        minWidth="990px"
        stickyFirstColumn
      />

      {data && <Pagination page={page} totalPages={data.totalPages} onChange={(next) => apply({}, next)} />}
    </div>
  );
}
