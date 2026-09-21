import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useQuery } from '@tanstack/react-query';
import { api } from '../../api/client';
import { queryKeys } from '../../app/queryKeys';
import { useAuth } from '../../context/AuthContext';
import { ErrorBanner } from '../../components/common/StateViews';
import Skeleton from '../../components/common/Skeleton';
import ChartFrame from '../../components/charts/ChartFrame';
import LineChart from '../../components/charts/LineChart';
import BarList from '../../components/charts/BarList';
import StatusDonut from '../../components/charts/StatusDonut';
import { formatPrice } from '../../components/product/ProductBadges';
import { formatDate } from '../../i18n';
import {
  hasNoActivity, isBrandNewStore, kpiCards, marginSummary, operationalAlerts, statusSlices, totalOrdersInPeriod, trendBucket, trendPoints,
} from '../../features/reporting/dashboardView';
import styles from './Dashboard.module.css';

const RANGES = ['Today', 'Last7Days', 'Last30Days', 'Last90Days', 'ThisYear'];

// ============================================================================
// لوحة المدير — "ماذا يجري في متجري الآن، وما الذي يحتاجني؟"
//
// كانت الشاشة عدّادَي منتجات وفئات مأخوذين من totalCount لنقاط عادية: صفحة تعدّ صفحات لا
// تقريراً. صارت الآن قراءة واحدة من وحدة Reporting (طلب واحد للوحة كلّها، فبطاقاتها من لحظة
// واحدة متّسقة).
//
// ترتيب الشاشة هو ترتيب الأسئلة: ما يحتاج تصرّفاً أوّلاً (تنبيهات)، ثم ماذا جرى (مؤشّرات
// ومنحنى)، ثم لماذا (الأكثر مبيعاً، الفئات، الحالات)، ثم حالة المخزون والعملاء.
//
// وكل رقم يحمل صيغته في عنوانه (title): لوحةٌ لا تقول ما تعنيه أرقامها تُقرأ خطأً بثقة.
// ============================================================================
export default function Dashboard() {
  const { t, i18n } = useTranslation();
  const { user } = useAuth();
  const [range, setRange] = useState('Last30Days');
  const rtl = i18n.dir() === 'rtl';

  const { data, error, isPending, refetch, isFetching } = useQuery({
    queryKey: queryKeys.storeDashboard(range),
    queryFn: () => api.getStoreDashboard(range),
    // بيانات تشغيلية: دقيقة واحدة تمنع موجة طلبات عند التنقّل بين المدد وتبقى حديثة بما يكفي.
    staleTime: 60_000,
  });

  const money = (value) => formatPrice(value, data?.currency);
  const cards = kpiCards(data);
  const alerts = operationalAlerts(data);
  const margin = marginSummary(data);
  const rangeLabel = t(`admin.reports.range.${range}`);

  return (
    <div className={styles.page}>
      <header className={styles.head}>
        <div>
          <h2 className={styles.title}>{t('admin.reports.title')}</h2>
          <p className={styles.subtitle}>
            {t('admin.dashboard.greeting', { name: user?.fullName || t('admin.adminFallback') })}
            {' — '}{t('admin.reports.subtitle')}
          </p>
        </div>

        {/* المدّة قائمة مغلقة: الخادم يحسب حدودها، والمتصفّح يرسل مفتاحاً لا تاريخين. */}
        <div className={styles.ranges} role="group" aria-label={t('admin.reports.rangeLabel')}>
          {RANGES.map((key) => (
            <button
              key={key}
              type="button"
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

      {isPending && (
        <div className={styles.kpiRow}>
          {[0, 1, 2, 3].map((i) => <Skeleton key={i} height={104} radius={14} />)}
        </div>
      )}

      {data && (
        <>
          {/* متجر جديد: إرشاد لا تقرير بأصفار. والفرق بين "جديد" و"هادئ" حقيقي — أحدهما
              لم يبدأ بعد، والآخر بدأ ولم يبع هذا الأسبوع. */}
          {isBrandNewStore(data) ? (
            <section className={styles.zeroState}>
              <h3>{t('admin.reports.emptyTitle')}</h3>
              <p>{t('admin.reports.emptyMessage')}</p>
              <Link to="/admin/products" className={styles.zeroAction}>{t('admin.dashboard.manageProducts')}</Link>
            </section>
          ) : (
            <>
              <section className={styles.alerts} aria-label={t('admin.reports.alertsTitle')}>
                <h3 className={styles.sectionTitle}>{t('admin.reports.alertsTitle')}</h3>
                {alerts.length === 0 ? (
                  <p className={styles.allClear}>{t('admin.reports.allClear')}</p>
                ) : (
                  <ul className={styles.alertList}>
                    {alerts.map((alert) => (
                      <li key={alert.key}>
                        <Link to={alert.href} className={`${styles.alert} ${styles[alert.tone]}`}>
                          {t(`admin.reports.alert.${alert.key}`, { count: alert.count })}
                        </Link>
                      </li>
                    ))}
                  </ul>
                )}
              </section>

              <div className={styles.kpiRow}>
                {cards.map((card) => (
                  <article key={card.key} className={styles.kpi} title={t(`admin.reports.formula.${card.formulaKey}`)}>
                    <span className={styles.kpiLabel}>{t(`admin.reports.kpi.${card.key}`)}</span>
                    <strong className={styles.kpiValue}>
                      {card.kind === 'money' ? money(card.value) : card.value}
                    </strong>
                    <span className={`${styles.kpiChange} ${styles[card.change.direction]}`}>
                      {card.change.kind === 'new' && t('admin.reports.firstSales')}
                      {card.change.kind === 'flat' && t('admin.reports.noChange')}
                      {card.change.kind === 'ratio' && (
                        <>
                          {card.change.direction === 'up' ? '▲' : '▼'} {card.change.percent}%
                          <span className={styles.kpiVs}>
                            {' '}{t('admin.reports.vsPrevious', { range: rangeLabel })}
                          </span>
                        </>
                      )}
                    </span>
                  </article>
                ))}
              </div>

              <div className={styles.grid}>
                <div className={styles.wide}>
                  <ChartFrame
                    title={t('admin.reports.trendTitle')}
                    hint={t(`admin.reports.trendHint${trendBucket(data) === 'month' ? 'Month' : 'Day'}`)}
                    loading={isFetching && !data}
                    isEmpty={hasNoActivity(data)}
                    emptyMessage={t('admin.reports.quietMessage')}
                    summary={t(`admin.reports.trendHint${trendBucket(data) === 'month' ? 'Month' : 'Day'}`)}
                    tableRows={trendPoints(data).map((p) => ({ label: formatDate(p.label), value: money(p.value) }))}
                  >
                    <LineChart
                      points={trendPoints(data)}
                      rtl={rtl}
                      formatValue={money}
                      formatLabel={(iso) => (iso ? formatDate(iso) : '')}
                    />
                  </ChartFrame>
                </div>

                <ChartFrame
                  title={t('admin.reports.statusTitle')}
                  hint={t('admin.reports.statusHint')}
                  isEmpty={totalOrdersInPeriod(data.ordersByStatus) === 0}
                  tableRows={statusSlices(data.ordersByStatus, t).map((s) => ({ label: s.label, value: s.value }))}
                >
                  <StatusDonut
                    items={statusSlices(data.ordersByStatus, t)}
                    total={totalOrdersInPeriod(data.ordersByStatus)}
                    totalLabel={t('admin.reports.statusTotal')}
                  />
                </ChartFrame>

                <ChartFrame
                  title={t('admin.reports.topProductsTitle')}
                  hint={t('admin.reports.topProductsHint')}
                  isEmpty={data.topProducts.length === 0}
                  tableRows={data.topProducts.map((p) => ({ label: p.name, value: money(p.revenue) }))}
                >
                  <BarList
                    rows={data.topProducts.map((p) => ({ id: p.productId, label: p.name, value: p.revenue, units: p.unitsSold }))}
                    formatValue={(row) => `${money(row.value)} · ${t('admin.reports.unitsSold', { count: row.units })}`}
                  />
                </ChartFrame>

                <ChartFrame
                  title={t('admin.reports.topCategoriesTitle')}
                  isEmpty={data.topCategories.length === 0}
                  tableRows={data.topCategories.map((c) => ({ label: c.name, value: money(c.revenue) }))}
                >
                  <BarList
                    rows={data.topCategories.map((c) => ({ id: c.categoryId, label: c.name, value: c.revenue, units: c.unitsSold }))}
                    formatValue={(row) => money(row.value)}
                  />
                </ChartFrame>

                <section className={styles.panel}>
                  <h3 className={styles.sectionTitle}>{t('admin.reports.inventoryTitle')}</h3>
                  <ul className={styles.factList}>
                    <li><span>{t('admin.reports.inventoryHealthy')}</span><b>{data.inventory.healthy}</b></li>
                    <li className={styles.warnRow}><span>{t('admin.reports.inventoryLow')}</span><b>{data.inventory.low}</b></li>
                    <li className={styles.dangerRow}><span>{t('admin.reports.inventoryOut')}</span><b>{data.inventory.outOfStock}</b></li>
                  </ul>
                </section>

                {/* الهامش، أو سببُ غيابه. لا يُعرض رقمٌ بلا تغطيته: تاجرٌ بلا تكاليف يُقال له
                    كيف يحصل على الرقم، لا يُعطى صفراً يبدو كأنه ربحه. */}
                <section className={styles.panel}>
                  <h3 className={styles.sectionTitle}>{t('admin.reports.marginTitle')}</h3>
                  {margin.known ? (
                    <ul className={styles.factList}>
                      <li><span>{t('admin.reports.grossProfit')}</span><b>{money(margin.grossProfit)}</b></li>
                      <li>
                        <span>{t('admin.reports.marginRatio')}</span>
                        <b>{new Intl.NumberFormat(i18n.language, { style: 'percent', maximumFractionDigits: 1 })
                          .format(margin.ratio)}</b>
                      </li>
                      {margin.partial && (
                        <li className={styles.warnRow}>
                          <span>{t('admin.reports.marginCoverage', {
                            percent: new Intl.NumberFormat(i18n.language, { style: 'percent', maximumFractionDigits: 0 })
                              .format(margin.coverageRatio),
                          })}</span>
                        </li>
                      )}
                    </ul>
                  ) : (
                    <p className={styles.emptyNote}>{t('admin.reports.marginUnknown')}</p>
                  )}
                </section>

                <section className={styles.panel}>
                  <h3 className={styles.sectionTitle}>{t('admin.reports.customersTitle')}</h3>
                  <ul className={styles.factList}>
                    <li><span>{t('admin.reports.totalCustomers')}</span><b>{data.totalCustomers}</b></li>
                    <li title={t('admin.reports.repeatHint')}>
                      <span>{t('admin.reports.repeatCustomers')}</span><b>{data.repeatCustomers}</b>
                    </li>
                  </ul>
                </section>
              </div>

              {/* ما لا تقيسه اللوحة يُقال صراحةً: غيابٌ مُعلَن أصدق من رقم مخترَع، وأنفع
                  لمن يقرأ — يعرف ما لا يستطيع أن يستنتجه من هنا. */}
              <details className={styles.limits}>
                <summary>{t('admin.reports.notMeasured')}</summary>
                <p>{t('admin.reports.noProfit')}</p>
                <p>{t('admin.reports.noConversion')}</p>
              </details>
            </>
          )}
        </>
      )}
    </div>
  );
}
