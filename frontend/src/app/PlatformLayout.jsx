import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useQuery } from '@tanstack/react-query';
import { api } from '../api/client';
import { queryKeys } from './queryKeys';
import { useAuth } from '../context/AuthContext';
import { ErrorBanner } from '../components/common/StateViews';
import Skeleton from '../components/common/Skeleton';
import styles from './PlatformLayout.module.css';

// حالات المتجر بترتيب دورة حياته — الترتيب معنى لا أبجدية.
const TENANT_STATUSES = ['Provisioning', 'Active', 'Suspended', 'Archived'];

// ============================================================================
// منطقة المنصّة (المرحلة 15، المنطقة الرابعة): تخطيطها وحارسها على مضيف المنصّة — لا على مضيف أي متجر (الخادم يرفض نقاطها هناك
// بـ 404).
//
// هذه النظرة أوّل مستهلك لـ GET /api/platform/stats، وهي نقطة قائمة منذ المرحلة 4 لم تكن لها
// شاشة. بقيّة شاشات المنصّة (المتاجر، التجهيز، الحسابات، سجلّ التدقيق) تبقى للمرحلة 18 —
// هذه شريحة منها لا استبدال لها، ولا يتغيّر ترقيم خارطة الطريق بسببها.
//
// ما يُعرض مجاميع عبر المتاجر لا صفوفاً: لا اسم متجر ولا رقم أعماله يظهر هنا. وحدود العدّ
// معروضة على الشاشة لأنها حقيقية — الأرقام تشمل المؤرشف والملغى والمعطّل، وهذا نادراً ما
// يكون ما يفترضه قارئ لوحة.
// ============================================================================
export default function PlatformLayout() {
  const { t } = useTranslation();
  const { user, logout } = useAuth();
  const navigate = useNavigate();

  const signOut = () => { logout(); navigate('/login', { replace: true }); };

  const { data, error, isPending, refetch } = useQuery({
    queryKey: queryKeys.platformStats(),
    queryFn: () => api.getPlatformStats(),
    staleTime: 60_000,
  });

  const totalStores = TENANT_STATUSES.reduce((sum, status) => sum + (data?.tenantsByStatus?.[status] ?? 0), 0);

  const figures = data ? [
    { key: 'stores', value: totalStores },
    { key: 'customers', value: data.customers },
    { key: 'products', value: data.products },
    { key: 'orders', value: data.orders },
    { key: 'ordersRecent', value: data.ordersLast30Days },
    { key: 'platformAccounts', value: data.platformAccounts },
    { key: 'storeStaff', value: data.storeStaffAccounts },
  ] : [];

  return (
    <div className={styles.shell}>
      <header className={styles.header}>
        <strong className={styles.brand}>{t('platform.name')}</strong>
        <div className={styles.account}>
          <span>{t('platform.signedInAs', { name: user?.fullName })}</span>
          <button type="button" className={styles.signOut} onClick={signOut}>{t('platform.logout')}</button>
        </div>
      </header>

      <main className={styles.content}>
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

            <p className={styles.roadmapNote}>{t('platform.subtitle')}</p>
          </>
        )}
      </main>
    </div>
  );
}
