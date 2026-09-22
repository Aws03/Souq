import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { api } from '../../api/client';
import { queryKeys } from '../../app/queryKeys';
import DataTable from '../../components/common/DataTable';
import Drawer from '../../components/common/Drawer';
import Pagination from '../../components/common/Pagination';
import RowActionsMenu from '../../components/common/RowActionsMenu';
import Skeleton from '../../components/common/Skeleton';
import StatusBadge from '../../components/common/StatusBadge';
import { ErrorBanner } from '../../components/common/StateViews';
import { formatPrice } from '../../components/product/ProductBadges';
import { statusTone } from '../../features/statusTone';
import { displayStatus } from '../../features/platform/invoices';
import { formatDate } from '../../i18n';
import admin from './Admin.module.css';
import styles from './Subscription.module.css';

const PAGE_SIZE = 20;

// ============================================================================
// اشتراكُ المتجر كما يراه تاجرُه (C5، ADR-0056): خطتُه، وما عليه، وفواتيرُه، وكيف يدفع.
//
// **وهي الوجهُ الآخر لدفتر المنصّة، لا نسخةٌ منه.** ما يراه التاجر هنا هو ما صدر إليه فحسب:
// المسوّدةُ التي يحرّرها مشغّلٌ الآن ليست مطالبةً بعد، والخادم لا يُرسلها أصلاً. ولا يظهر في هذه
// الشاشة شيءٌ عن متجرٍ آخر ولا عن إعداد المنصّة — المتجر يأتي من المضيف، فـ«فاتورة تاجرٍ آخر»
// غيرُ قابلة للطلب لا مرفوضةٌ بفحص.
//
// **ولا زرَّ دفعٍ هنا، وذلك هو التصميم لا نقصٌ فيه.** التحصيلُ حوالةٌ بنكية بقرار المالك `C-15`،
// فما يحتاجه التاجر هو أن يقرأ المبلغ وتعليماتِ التحويل — والمشغّل يسجّل الحوالة حين تصل.
// ============================================================================
export default function Subscription() {
  const { t } = useTranslation();
  const [page, setPage] = useState(1);
  const [openInvoiceId, setOpenInvoiceId] = useState(null);

  const summary = useQuery({
    queryKey: queryKeys.mySubscription(),
    queryFn: api.getMySubscription,
  });

  const params = { page, pageSize: PAGE_SIZE };
  const invoices = useQuery({
    queryKey: queryKeys.myInvoices(params),
    queryFn: () => api.getMyInvoices(params),
    placeholderData: keepPreviousData,
  });

  const columns = [
    {
      key: 'number',
      header: t('admin.subscription.colNumber'),
      width: '150px',
      truncate: true,
      render: (row) => <span dir="ltr">{row.number}</span>,
    },
    {
      key: 'status',
      header: t('admin.subscription.colStatus'),
      width: '130px',
      render: (row) => (
        <StatusBadge tone={statusTone('invoice', displayStatus(row))}>
          {t(`admin.subscription.status.${displayStatus(row)}`)}
        </StatusBadge>
      ),
    },
    {
      key: 'total',
      header: t('admin.subscription.colTotal'),
      width: '130px',
      align: 'end',
      render: (row) => formatPrice(row.total, row.currency),
    },
    {
      key: 'outstanding',
      header: t('admin.subscription.colOutstanding'),
      width: '130px',
      align: 'end',
      render: (row) => (row.outstanding > 0
        ? <span className={styles.attention}>{formatPrice(row.outstanding, row.currency)}</span>
        : formatPrice(0, row.currency)),
    },
    {
      key: 'issued',
      header: t('admin.subscription.colIssued'),
      width: '110px',
      render: (row) => (row.issuedAtUtc ? formatDate(row.issuedAtUtc) : '—'),
    },
    {
      key: 'due',
      header: t('admin.subscription.colDue'),
      width: '150px',
      render: (row) => {
        if (!row.dueAtUtc) return '—';
        return row.isOverdue
          ? <span className={styles.attention}>{t('admin.subscription.overdueBy', { count: row.daysOverdue })}</span>
          : formatDate(row.dueAtUtc);
      },
    },
    {
      key: 'action',
      header: t('admin.subscription.colAction'),
      width: '90px',
      align: 'end',
      // نمطُ بقيّة جداول اللوحة: الدرجُ يُفتح من قائمة الصفّ لا من رابطٍ في خليّة.
      render: (row) => (
        <RowActionsMenu
          label={t('admin.subscription.rowActions')}
          actions={[{ label: t('admin.subscription.view'), onClick: () => setOpenInvoiceId(row.id) }]}
        />
      ),
    },
  ];

  return (
    <div>
      <h1 className={admin.pageTitle}>{t('admin.subscription.title')}</h1>
      <p className={admin.pageSub}>{t('admin.subscription.subtitle')}</p>

      {summary.error && <ErrorBanner message={summary.error.message} onRetry={summary.refetch} />}
      {summary.isPending && <Skeleton height={140} radius={14} />}

      {summary.data && (
        <>
          <dl className={admin.statRow}>
            <div className={admin.statTile}>
              <dt>{t('admin.subscription.plan')}</dt>
              <dd>{summary.data.planName ?? t('admin.subscription.noPlan')}</dd>
            </div>
            <div className={admin.statTile}>
              <dt>{t('admin.subscription.price')}</dt>
              <dd>
                {summary.data.priceAmount === null || summary.data.priceAmount === undefined
                  ? t('admin.subscription.noPrice')
                  : t('admin.subscription.pricePerInterval', {
                    price: formatPrice(summary.data.priceAmount, summary.data.priceCurrency),
                    count: summary.data.billingIntervalMonths,
                  })}
              </dd>
            </div>
            <div className={admin.statTile}>
              <dt>{t('admin.subscription.outstanding')}</dt>
              <dd>
                {summary.data.outstandingTotal > 0
                  ? <span className={styles.attention}>
                    {formatPrice(summary.data.outstandingTotal, summary.data.outstandingCurrency)}
                  </span>
                  : formatPrice(0, summary.data.outstandingCurrency)}
              </dd>
            </div>
            <div className={admin.statTile}>
              <dt>{t('admin.subscription.openInvoices')}</dt>
              <dd>
                {t('admin.subscription.openCount', { count: summary.data.openInvoiceCount })}
                {summary.data.overdueInvoiceCount > 0 && (
                  <> · <span className={styles.attention}>
                    {t('admin.subscription.overdueCount', { count: summary.data.overdueInvoiceCount })}
                  </span></>
                )}
              </dd>
            </div>
          </dl>

          {/* تعليماتُ الدفع الحالية — لا المجمَّدة على فاتورةٍ بعينها: السؤال هنا «كيف أدفع الآن؟»،
              وحسابٌ بنكيّ تغيّر يجب أن يظهر فوراً. والمجمَّد على كل فاتورة يبقى كما كان يوم صدرت. */}
          {summary.data.paymentInstructions && summary.data.outstandingTotal > 0 && (
            <section className={styles.panel} aria-labelledby="how-to-pay">
              <h2 id="how-to-pay" className={styles.panelTitle}>{t('admin.subscription.howToPay')}</h2>
              <p className={styles.preLine}>{summary.data.paymentInstructions}</p>
            </section>
          )}
        </>
      )}

      <h2 className={styles.panelTitle}>{t('admin.subscription.invoices')}</h2>
      <DataTable
        label={t('admin.subscription.invoices')}
        columns={columns}
        rows={invoices.data?.items ?? []}
        rowKey={(row) => row.id}
        loading={invoices.isPending}
        error={invoices.error?.message}
        onRetry={invoices.refetch}
        emptyTitle={t('admin.subscription.emptyTitle')}
        emptyMessage={t('admin.subscription.emptyMessage')}
        minWidth="890px"
        stickyFirstColumn
      />
      {invoices.data && <Pagination page={page} totalPages={invoices.data.totalPages} onChange={setPage} />}

      {openInvoiceId && (
        <InvoiceDrawer invoiceId={openInvoiceId} onClose={() => setOpenInvoiceId(null)} />
      )}
    </div>
  );
}

// درجُ الفاتورة: المستند كما صدر — بمُصدِره وتعليمات دفعه **المجمَّدة عليه**، لا بقيم اليوم.
function InvoiceDrawer({ invoiceId, onClose }) {
  const { t } = useTranslation();
  const { data, error, isPending, refetch } = useQuery({
    queryKey: queryKeys.myInvoice(invoiceId),
    queryFn: () => api.getMyInvoice(invoiceId),
  });

  const money = (amount) => formatPrice(amount, data?.currency);

  return (
    <Drawer open onClose={onClose} title={t('admin.subscription.invoiceTitle')}>
      {error && <ErrorBanner message={error.message} onRetry={refetch} />}
      {isPending && <Skeleton height={320} radius={12} />}

      {data && (
        <div>
          <p className={admin.pageTitle}><span dir="ltr">{data.number}</span></p>
          <p>
            <StatusBadge tone={statusTone('invoice', displayStatus(data))}>
              {t(`admin.subscription.status.${displayStatus(data)}`)}
            </StatusBadge>
          </p>

          <dl className={styles.facts}>
            <dt>{t('admin.subscription.period')}</dt>
            <dd>{formatDate(data.periodStartUtc)} — {formatDate(data.periodEndUtc)}</dd>
            {data.issuedAtUtc && (<>
              <dt>{t('admin.subscription.issuedAt')}</dt>
              <dd>{formatDate(data.issuedAtUtc)}</dd>
            </>)}
            {data.dueAtUtc && (<>
              <dt>{t('admin.subscription.dueAt')}</dt>
              <dd>
                {formatDate(data.dueAtUtc)}
                {data.isOverdue && (
                  <> <span className={styles.attention}>
                    {t('admin.subscription.overdueBy', { count: data.daysOverdue })}
                  </span></>
                )}
              </dd>
            </>)}
            {data.issuerName && (<>
              <dt>{t('admin.subscription.issuer')}</dt>
              <dd>{data.issuerName}</dd>
            </>)}
          </dl>

          <h3 className={styles.panelTitle}>{t('admin.subscription.lines')}</h3>
          <ul className={styles.rows}>
            {data.lines.map((line) => (
              <li key={line.id} className={styles.row}>
                <span>{line.description}</span>
                <span>
                  {t('admin.subscription.lineQuantity', { quantity: line.quantity })}
                  {' · '}
                  <strong>{money(line.lineTotal)}</strong>
                </span>
              </li>
            ))}
          </ul>

          <dl className={styles.facts}>
            <dt>{t('admin.subscription.subtotal')}</dt>
            <dd>{money(data.subtotal)}</dd>
            {/* سطرُ الضريبة يظهر حين تُجمَع فقط — وبلا ذلك يسأل التاجر عن صفرٍ لا جواب له،
                وهي القاعدةُ نفسها التي تتبعها سلّة المتسوّق. */}
            {data.taxAmount > 0 && (<>
              <dt>{t('admin.subscription.tax')}</dt>
              <dd>{money(data.taxAmount)}</dd>
            </>)}
            <dt>{t('admin.subscription.total')}</dt>
            <dd><strong>{money(data.total)}</strong></dd>
            <dt>{t('admin.subscription.paid')}</dt>
            <dd>{money(data.amountPaid)}</dd>
            {data.credited > 0 && (<>
              <dt>{t('admin.subscription.credited')}</dt>
              <dd>{money(data.credited)}</dd>
            </>)}
            <dt>{t('admin.subscription.outstanding')}</dt>
            <dd><strong>{money(data.outstanding)}</strong></dd>
          </dl>

          {data.paymentInstructions && data.outstanding > 0 && (
            <>
              <h3 className={styles.panelTitle}>{t('admin.subscription.howToPay')}</h3>
              <p className={styles.preLine}>{data.paymentInstructions}</p>
            </>
          )}

          {data.creditNotes.length > 0 && (
            <>
              <h3 className={styles.panelTitle}>{t('admin.subscription.creditNotes')}</h3>
              <ul className={styles.rows}>
                {data.creditNotes.map((note) => (
                  <li key={note.id} className={styles.row}>
                    <span dir="ltr">{note.number}</span>
                    <span>{money(note.total)}{note.reason && <> · {note.reason}</>}</span>
                  </li>
                ))}
              </ul>
            </>
          )}
        </div>
      )}
    </Drawer>
  );
}
