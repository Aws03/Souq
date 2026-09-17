import { Link, useParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import Skeleton from '../../components/common/Skeleton';
import { ErrorBanner } from '../../components/common/StateViews';
import { formatDate } from '../../i18n';
import { resumeStep } from '../../features/platform/provisioning';
import { usePlatformStore, useProvisioningOptions } from '../../features/platform/usePlatformStore';
import {
  AdminsPanel, DomainsPanel, LifecyclePanel, ModulesPanel, ProfilePanel, ReadinessPanel, StatusBadge,
} from './StorePanels';
import styles from './Platform.module.css';

// ============================================================================
// متجر واحد من المنصّة: جاهزيته، وملفه (الاسم والعملة)، ونطاقاته، ووحداته، ومديروه، ودورة حياته — والإعدادات
// في صفحتها (محرّر طويل لا يُحشر هنا). الأقسام نفسها التي يمرّ بها المعالج.
// ما لا يظهر هنا عمداً: بيانات المتجر التجارية (طلباته، عملاؤه) — تلك للمتجر، لا لمنطقة المنصّة.
// ============================================================================
export default function StoreDetail() {
  const { t } = useTranslation();
  const { id } = useParams();
  const options = useProvisioningOptions();
  const { store, accounts, refresh } = usePlatformStore(id);

  const error = store.error ?? accounts.error ?? options.error;
  if (error) {
    return error.status === 404
      ? <ErrorBanner message={t('platform.store.notFound')} />
      : <ErrorBanner message={error.message} onRetry={() => { store.refetch(); accounts.refetch(); options.refetch(); }} />;
  }
  if (!store.data || !accounts.data || !options.data) return <Skeleton height={320} radius={14} />;

  const detail = store.data;
  const accountList = accounts.data.items;
  const primary = detail.domains.find((d) => d.isPrimary);

  return (
    <div className={styles.setup}>
      <Link to="/platform/stores" className={styles.back}>{t('platform.setup.backToStores')}</Link>
      <div className={styles.pageHead}>
        <div>
          <h1 className={styles.title}>{detail.name}</h1>
          <p className={styles.subtitle}><StatusBadge status={detail.status} /></p>
        </div>
        <div className={styles.headLinks}>
          {detail.status === 'Provisioning' && (
            <Link to={`/platform/stores/${id}/setup/${resumeStep(detail, accountList)}`} className={styles.primaryLink}>
              {t('platform.stores.continueSetup')}
            </Link>
          )}
          <Link to={`/platform/stores/${id}/settings`} className={styles.secondaryLink}>{t('platform.store.editSettings')}</Link>
        </div>
      </div>

      <dl className={styles.facts}>
        <div><dt>{t('platform.identity.slug')}</dt><dd dir="ltr">{detail.slug}</dd></div>
        <div><dt>{t('platform.store.primaryHost')}</dt><dd dir="ltr">{primary?.host ?? '—'}</dd></div>
        <div><dt>{t('platform.identity.defaultCulture')}</dt><dd>{t(`admin.settings.culture.${detail.defaultCulture}`)}</dd></div>
        <div><dt>{t('admin.settings.timeZone')}</dt><dd dir="ltr">{detail.timeZone}</dd></div>
        <div><dt>{t('platform.stores.colCreated')}</dt><dd>{formatDate(detail.createdAt)}</dd></div>
      </dl>

      <ReadinessPanel store={detail} accounts={accountList} />
      <ProfilePanel key={`${detail.name}|${detail.currency}`} store={detail} options={options.data} onChanged={refresh} />
      <DomainsPanel store={detail} options={options.data} onChanged={refresh} />
      <ModulesPanel key={detail.modules.join()} store={detail} options={options.data} onChanged={refresh} />
      <AdminsPanel store={detail} accounts={accountList} options={options.data} onChanged={refresh} />
      <LifecyclePanel store={detail} accounts={accountList} onChanged={refresh} />
    </div>
  );
}
