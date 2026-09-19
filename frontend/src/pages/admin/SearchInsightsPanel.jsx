import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { api } from '../../api/client';
import { queryKeys } from '../../app/queryKeys';
import { formatDateTime } from '../../i18n';
import DataTable from '../../components/common/DataTable';
import Pagination from '../../components/common/Pagination';
import StatusBadge from '../../components/common/StatusBadge';
import Button from '../../components/common/Button';
import { insightOutcome, zeroResultShare } from '../../features/admin/search/insights';
import styles from './Admin.module.css';

const PAGE_SIZE = 20;
const WINDOWS = [7, 30, 90];

// ============================================================================
// ما بحث عنه زبائن المتجر (M13) — نصف الحلقة التي بدأها M3.
//
// M3 أعطى التاجر محرّراً للمفردات ولم يُعطه سبباً يفتحه به: لا يعرف أيّ كلمةٍ يكتبها زبائنه ولا يجدونها.
// هذه الشاشة هي السبب، ولهذا **زرّ "أضِفها مرادفاً" في كل صفّ**: الطريق من "هذه الكلمة تفشل" إلى "علّمتُ
// المحرّك إيّاها" نقرةٌ واحدة، لا شاشتان يُنسَخ بينهما نصّ.
//
// **والافتراض هو "لم تجد شيئاً"** لا "كل الكلمات": قائمةُ كل ما بُحث عنه معلومةٌ، أمّا الكلمات التي تفشل
// دائماً فهي عملٌ — والشاشة تفتح على العمل. ومن أراد المعلومة يُطفئ المرشّح.
//
// **واتجاه كل كلمة من لغتها لا من لغة الواجهة**: تاجرٌ يعمل بواجهة عربية يقرأ كلمةً كتبها زبونٌ
// بالإنجليزية، وعرضُها في سياق RTL يُقلب ترتيب حروفها المرئي. نفس قاعدة درج المفردات في M3.
// ============================================================================
export default function SearchInsightsPanel({ onAddSynonym }) {
  const { t, i18n } = useTranslation();
  const [days, setDays] = useState(30);
  const [onlyZeroResults, setOnlyZeroResults] = useState(true);
  const [page, setPage] = useState(1);

  const params = { days, onlyZeroResults, page, pageSize: PAGE_SIZE };
  const { data, error, isPending, refetch } = useQuery({
    queryKey: queryKeys.searchInsights(params),
    queryFn: () => api.getSearchInsights(params),
    placeholderData: keepPreviousData,
  });

  // تغيير مرشّح يعيد إلى الصفحة الأولى — في المُعالج لا في `useEffect`: أثرٌ جانبي على تغيّرٍ مشتَقّ
  // يُشغّل جلبتين (القديمة ثم المصحّحة) ويُظهر صفحةً فارغة بينهما (نفس ما صُحِّح في Inventory في M7).
  const filterBy = (set) => (value) => { set(value); setPage(1); };

  const summary = data?.summary;
  const share = zeroResultShare(summary);
  const dirOf = (row) => (row.culture === 'en' ? 'ltr' : 'rtl');
  const number = (value) => (value ?? 0).toLocaleString(i18n.language);

  const columns = [
    {
      key: 'term', header: t('admin.searchInsights.colTerm'), width: '200px', truncate: true,
      tooltip: (row) => row.term,
      render: (row) => <span dir={dirOf(row)}>{row.term}</span>,
    },
    {
      key: 'matched', header: t('admin.searchInsights.colMatched'), width: '170px', truncate: true,
      tooltip: (row) => row.termNormalized,
      render: (row) => <span className={styles.muted} dir={dirOf(row)}>{row.termNormalized}</span>,
    },
    {
      key: 'searches', header: t('admin.searchInsights.colSearches'), width: '90px', align: 'end',
      render: (row) => number(row.searches),
    },
    {
      // الحالة لا العدد المجرّد: القرار الذي يتّخذه التاجر هنا هو "أعملُ على هذه أم لا". الثلاث حالات
      // وقواعدها في `insightOutcome` لا في هذا الصفّ — منطقٌ يُختبر، وأخطاؤه (صفرٌ يُعرَض تحذيراً) تُمسك.
      key: 'outcome', header: t('admin.searchInsights.colOutcome'), width: '170px',
      render: (row) => {
        const outcome = insightOutcome(row);
        return (
          <StatusBadge tone={outcome.tone}>
            {t(`admin.searchInsights.${outcome.key}`, { count: outcome.count })}
          </StatusBadge>
        );
      },
    },
    {
      key: 'last', header: t('admin.searchInsights.colLastSearched'), width: '160px',
      render: (row) => <span className={styles.muted}>{formatDateTime(row.lastSearchedAt)}</span>,
    },
    {
      key: 'actions', header: t('admin.searchInsights.colAction'), width: '150px', align: 'end',
      render: (row) => (
        <Button variant="ghost" size="sm" onClick={() => onAddSynonym(row)}>
          {t('admin.searchInsights.addSynonym')}
        </Button>
      ),
    },
  ];

  return (
    <div>
      {/* ثلاثة أرقام للنافذة كلّها لا للصفحة: التاجر يسأل أولاً "هل بحثي يعمل؟" ثمّ ينظر في التفاصيل. */}
      <div className={styles.statRow}>
        <div className={styles.statTile}>
          <div className={styles.statValue}>{number(summary?.totalSearches)}</div>
          <div className={styles.statLabel}>{t('admin.searchInsights.totalSearches')}</div>
        </div>
        <div className={styles.statTile}>
          <div className={styles.statValue}>{number(summary?.distinctTerms)}</div>
          <div className={styles.statLabel}>{t('admin.searchInsights.distinctTerms')}</div>
        </div>
        <div className={styles.statTile}>
          {/* لا بحوث في النافذة ⇒ شرطةٌ لا صفر: "٠٪ تفشل" دعوى، والصحيح أنّه لا شيء يُقاس. */}
          <div className={styles.statValue}>
            {share === null ? '—' : t('admin.searchInsights.percent', { value: share.toLocaleString(i18n.language, { maximumFractionDigits: 1 }) })}
          </div>
          <div className={styles.statLabel}>{t('admin.searchInsights.zeroResultShare')}</div>
        </div>
      </div>

      <div className={styles.toolbar}>
        <select value={days} onChange={(e) => filterBy(setDays)(Number(e.target.value))}
          aria-label={t('admin.searchInsights.windowLabel')}>
          {WINDOWS.map((value) => (
            <option key={value} value={value}>{t('admin.searchInsights.windowDays', { count: value })}</option>
          ))}
        </select>

        <label className={styles.checkFilter}>
          <input type="checkbox" checked={onlyZeroResults}
            onChange={(e) => filterBy(setOnlyZeroResults)(e.target.checked)} />
          <span>{t('admin.searchInsights.onlyZeroResults')}</span>
        </label>
      </div>

      <DataTable label={t('admin.searchInsights.tab')} columns={columns} rows={data?.items ?? []} rowKey={(row) => `${row.culture}:${row.termNormalized}`}
        loading={isPending} error={error?.message} onRetry={refetch}
        emptyTitle={t(onlyZeroResults ? 'admin.searchInsights.emptyFailingTitle' : 'admin.searchInsights.emptyTitle')}
        emptyMessage={t(onlyZeroResults ? 'admin.searchInsights.emptyFailingMessage' : 'admin.searchInsights.emptyMessage')}
        minWidth="920px" stickyFirstColumn />

      {data && <Pagination page={data.pageNumber} pageSize={PAGE_SIZE} total={data.totalCount} onChange={setPage} />}
    </div>
  );
}
