import { Link, useParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import Skeleton from '../../components/common/Skeleton';
import { ErrorBanner } from '../../components/common/StateViews';
import { usePlatformStore } from '../../features/platform/usePlatformStore';
import PlatformStoreSettings from './PlatformStoreSettings';
import styles from './Platform.module.css';

// إعدادات متجر من المنصّة، في صفحتها — المحرّر نفسه الذي يستعمله مدير المتجر.
export default function StoreSettingsPage() {
  const { t } = useTranslation();
  const { id } = useParams();
  const { store, refresh } = usePlatformStore(id);

  if (store.error) {
    return store.error.status === 404
      ? <ErrorBanner message={t('platform.store.notFound')} />
      : <ErrorBanner message={store.error.message} onRetry={store.refetch} />;
  }
  if (!store.data) return <Skeleton height={320} radius={14} />;

  return (
    <div className={styles.setup}>
      <Link to={`/platform/stores/${id}`} className={styles.back}>{t('platform.store.backToStore', { name: store.data.name })}</Link>
      <h1 className={styles.title}>{t('platform.store.settingsTitle', { name: store.data.name })}</h1>
      <p className={styles.subtitle}>{t('platform.store.settingsSubtitle')}</p>
      <PlatformStoreSettings store={store.data} onSaved={refresh} />
    </div>
  );
}
