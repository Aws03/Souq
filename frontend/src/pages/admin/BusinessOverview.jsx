import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useQuery } from '@tanstack/react-query';
import { api } from '../../api/client';
import { queryKeys } from '../../app/queryKeys';
import { ErrorBanner } from '../../components/common/StateViews';
import Skeleton from '../../components/common/Skeleton';
import ChartFrame from '../../components/charts/ChartFrame';
import LineChart from '../../components/charts/LineChart';
import BarList from '../../components/charts/BarList';
import { formatPrice } from '../../components/product/ProductBadges';
import { formatDate } from '../../i18n';
import { trendPoints } from '../../features/reporting/dashboardView';
import { businessHealth, headlineMetrics, repeatRate, riskSignals } from '../../features/reporting/businessHealth';
import styles from './BusinessOverview.module.css';

const RANGES = ['Last30Days', 'Last90Days', 'ThisYear'];

// ============================================================================
// نظرة العمل — لمالكٍ أو شريكٍ أو مستثمر، لا لمن يدير المتجر يومياً.
//
// لماذا شاشة منفصلة عن لوحة المدير وليست تبويباً فيها؟ لأن السؤال مختلف. المدير يسأل "ما
// الذي يحتاجني الآن؟" فيريد الطلبات المعلّقة والمخزون المنخفض. وهذا القارئ يسأل "هل العمل
// سليم وينمو وأين الخطر؟" ولا يعرف معنى "محجوز من المخزون" ولا يحتاج أن يعرف. لوحةٌ تخدم
// السؤالين تخدم أحدهما رديئاً.
//
// ولا رقم جديد هنا: المصدر هو استجابة اللوحة نفسها. لوحتان بأرقام مختلفة للشيء نفسه تعني
// أن إحداهما تكذب — والصلاحية نفسها (store.reports.view) لأن البيانات هي هي.
//
// المدد هنا أطول (٣٠ يوماً فما فوق): لا معنى لسؤال "هل ينمو العمل؟" عن يوم واحد.
// ============================================================================
export default function BusinessOverview() {
  const { t, i18n } = useTranslation();
  const [range, setRange] = useState('Last90Days');
  const rtl = i18n.dir() === 'rtl';

  const { data, error, isPending, refetch } = useQuery({
    queryKey: queryKeys.storeDashboard(range),
    queryFn: () => api.getStoreDashboard(range),
    staleTime: 60_000,
  });

  const money = (value) => formatPrice(value, data?.currency);
  const health = businessHealth(data);
  const metrics = headlineMetrics(data);
  const risks = riskSignals(data);
  const loyalty = repeatRate(data);

  return (
    <div className={styles.page}>
      <header className={styles.head}>
        <div>
          <h2 className={styles.title}>{t('admin.business.title')}</h2>
          <p className={styles.subtitle}>{t('admin.business.subtitle')}</p>
        </div>
        <div className={styles.ranges} role="group" aria-label={t('admin.reports.rangeLabel')}>
          {RANGES.map((key) => (
            <button
              key={key} type="button"
              className={`${styles.rangeBtn} ${key === range ? styles.rangeActive : ''}`}
              aria-pressed={key === range}
              onClick={() => setRange(key)}
            >
              {t(`admin.reports.range.${key}`)}
            </button>
          ))}
        </div>
      </header>

      {error && <ErrorBanner message={error.message} onRetry={refetch} />}
      {isPending && <Skeleton height={140} radius={14} />}

      {data && (
        <>
          {/* الحكم أوّلاً وبسببه في السطر نفسه: حكمٌ بلا سبب رأيٌ لا يستطيع القارئ مراجعته. */}
          <section className={`${styles.verdict} ${styles[health.level]}`}>
            <span className={styles.verdictBadge}>{t(`admin.business.health.${health.level}`)}</span>
            <p className={styles.verdictReason}>
              {t(`admin.business.reason.${health.reasonKey}`, health.values ?? {})}
            </p>
          </section>

          <div className={styles.metricRow}>
            {metrics.map((metric) => (
              <article key={metric.key} className={styles.metric}>
                <span className={styles.metricLabel}>{t(`admin.business.metric.${metric.key}`)}</span>
                <strong className={styles.metricValue}>
                  {metric.kind === 'money' ? money(metric.value) : metric.value}
                </strong>
                <span className={`${styles.metricTrend} ${styles[metric.trend.direction]}`}>
                  {metric.trend.kind === 'ratio' && `${metric.trend.direction === 'up' ? '▲' : metric.trend.direction === 'down' ? '▼' : '='} ${metric.trend.percent}%`}
                  {metric.trend.kind === 'new' && t('admin.reports.firstSales')}
                  {metric.trend.kind === 'flat' && t('admin.reports.noChange')}
                </span>
                {/* الشرح بجانب الرقم لا في تلميح يحتاج فأرة: هذا القارئ لا يعرف مفرداتنا. */}
                <p className={styles.metricHint}>{t(`admin.business.metricHint.${metric.key}`)}</p>
              </article>
            ))}
          </div>

          <div className={styles.grid}>
            <div className={styles.wide}>
              <ChartFrame
                title={t('admin.business.revenueTrend')}
                isEmpty={data.current.orders === 0}
                emptyMessage={t('admin.business.reason.noSales')}
                tableRows={trendPoints(data).map((p) => ({ label: formatDate(p.label), value: money(p.value) }))}
              >
                <LineChart
                  points={trendPoints(data)} rtl={rtl}
                  formatValue={money} formatLabel={(iso) => (iso ? formatDate(iso) : '')}
                />
              </ChartFrame>
            </div>

            <ChartFrame
              title={t('admin.business.topProducts')}
              isEmpty={data.topProducts.length === 0}
              tableRows={data.topProducts.map((p) => ({ label: p.name, value: money(p.revenue) }))}
            >
              <BarList
                rows={data.topProducts.map((p) => ({ id: p.productId, label: p.name, value: p.revenue }))}
                formatValue={(row) => money(row.value)}
              />
            </ChartFrame>

            <section className={styles.panel}>
              <h3 className={styles.panelTitle}>{t('admin.business.loyaltyTitle')}</h3>
              {/* "لا نعرف بعد" ليست "صفر بالمئة" — والفرق حقيقي لمتجر بلا عملاء. */}
              <p className={styles.loyalty}>
                {loyalty === null
                  ? t('admin.business.repeatUnknown')
                  : t('admin.business.repeatRate', { percent: loyalty })}
              </p>
            </section>

            <section className={styles.panel}>
              <h3 className={styles.panelTitle}>{t('admin.business.risksTitle')}</h3>
              {risks.length === 0 ? (
                <p className={styles.noRisks}>{t('admin.business.noRisks')}</p>
              ) : (
                <ul className={styles.riskList}>
                  {risks.map((risk) => (
                    <li key={risk.key} className={styles[`risk-${risk.severity}`]}>
                      {t(`admin.business.reason.${risk.key}`, risk.values)}
                    </li>
                  ))}
                </ul>
              )}
            </section>
          </div>

          {/* الحدود معروضة لا مطويّة هنا: قارئٌ غير تقنيّ قد يفترض أن "الإيراد" ربح، وأن
              الأرقام تتنبّأ. قول العكس صراحةً جزء من أمانة الصفحة. */}
          <section className={styles.limits}>
            <h3 className={styles.panelTitle}>{t('admin.business.limitsTitle')}</h3>
            <ul>
              <li>{t('admin.business.noProfit')}</li>
              <li>{t('admin.business.noConversion')}</li>
              <li>{t('admin.business.noForecast')}</li>
            </ul>
          </section>
        </>
      )}
    </div>
  );
}
