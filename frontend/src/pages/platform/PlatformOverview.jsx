import { useTranslation } from 'react-i18next';
import { useQuery } from '@tanstack/react-query';
import { api } from '../../api/client';
import { queryKeys } from '../../app/queryKeys';
import { ErrorBanner } from '../../components/common/StateViews';
import Skeleton from '../../components/common/Skeleton';
import styles from './Platform.module.css';

// حالات المتجر بترتيب دورة حياته — الترتيب معنى لا أبجدية.
const TENANT_STATUSES = ['Provisioning', 'Active', 'Suspended', 'Archived'];

// ============================================================================
// نظرة المنصّة (platform.reports.view): أوّل مستهلك لـ GET /api/platform/stats.
//
// ما يُعرض مجاميع عبر المتاجر لا صفوفاً: لا اسم متجر ولا رقم أعماله يظهر هنا. وحدود العدّ
// معروضة على الشاشة لأنها حقيقية — الأرقام تشمل المؤرشف والملغى والمعطّل، وهذا نادراً ما
// يكون ما يفترضه قارئ لوحة. قائمة المتاجر نفسها في شاشة المتاجر، بصلاحيتها هي.
// ============================================================================
export default function PlatformOverview() {
  const { t } = useTranslation();

  const { data, error, isPending, refetch } = useQuery({
    queryKey: queryKeys.platformStats(),
    queryFn: () => api.getPlatformStats(),
    staleTime: 60_000,
  });

  const totalStores = TENANT_STATUSES.reduce((sum, status) => sum + (data?.tenantsByStatus?.[status] ?? 0), 0);

  const figures = data ? [
    // `storesTotal` لا `stores`: الثانية **كائن** (نصوص صفحة المتاجر)، وi18next يعيد المفتاح نفسه
    // عند طلب كائنٍ نصّاً — فكانت أول بطاقة في صفحة مالك المنصّة تعرض "platform.stores" حرفيّاً
    // بلا عنوان (M12). ولم يمسكها شيء: اختبار الصفحة يُبدِل `t` بدالّة هويّة فيُوكّد المفتاح الخام،
    // وفاحصُ المفاتيح يقرأ نداءات `t('…')` الحرفية وحدها فلا يرى مفتاحاً مبنيّاً بقالب.
    { key: 'storesTotal', value: totalStores },
    { key: 'customers', value: data.customers },
    { key: 'products', value: data.products },
    { key: 'orders', value: data.orders },
    { key: 'ordersRecent', value: data.ordersLast30Days },
    { key: 'platformAccounts', value: data.platformAccounts },
    { key: 'storeStaff', value: data.storeStaffAccounts },
  ] : [];

  return (
    <>
        <h1 className={styles.title}>{t('platform.overviewTitle')}</h1>
        <p className={styles.subtitle}>{t('platform.overviewSubtitle')}</p>

        {error && <ErrorBanner message={error.message} onRetry={refetch} />}
        {isPending && <Skeleton height={120} radius={14} />}

        {data && (
          <>
            <div className={styles.figures}>
              {figures.map((figure) => (
                <article key={figure.key} className={styles.figure}>
                  <span className={styles.figureLabel}>{t(`platform.${figure.key}`)}</span>
                  <strong className={styles.figureValue}>{figure.value}</strong>
                </article>
              ))}
            </div>

            <section className={styles.panel}>
              <h2 className={styles.panelTitle}>{t('platform.storesByStatus')}</h2>
              <ul className={styles.statusList}>
                {TENANT_STATUSES.map((status) => (
                  <li key={status}>
                    <span>{t(`platform.status.${status}`)}</span>
                    <b>{data.tenantsByStatus?.[status] ?? 0}</b>
                  </li>
                ))}
              </ul>
            </section>

            {/* حدود العدّ معروضة لا مطويّة: قارئٌ يفترض أن "الطلبات" تعني المكتملة سيقرأ خطأً. */}
            <section className={styles.limits}>
              <h2 className={styles.panelTitle}>{t('platform.countingTitle')}</h2>
              <p>{t('platform.countingNote')}</p>
              <p>{t('platform.noTenantData')}</p>
            </section>

          </>
        )}
    </>
  );
}
