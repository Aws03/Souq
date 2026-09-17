import { useState } from 'react';
import { Link, Navigate, useNavigate, useParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import Button from '../../components/common/Button';
import Skeleton from '../../components/common/Skeleton';
import { ErrorBanner } from '../../components/common/StateViews';
import { SETUP_STEPS, nextStep, previousStep } from '../../features/platform/provisioning';
import { usePlatformStore, useProvisioningOptions } from '../../features/platform/usePlatformStore';
import PlatformStoreSettings from './PlatformStoreSettings';
import SetupProgress from './SetupProgress';
import {
  AdminsPanel, DomainsPanel, LifecyclePanel, ModulesPanel, ReadinessPanel, StatusBadge,
} from './StorePanels';
import styles from './Platform.module.css';

// ============================================================================
// معالج التجهيز بعد الإنشاء: الهوية البصرية ← النطاقات ← الوحدات ← المدير ← المراجعة والتفعيل.
//
// كل خطوة قسمٌ من صفحة المتجر نفسها (StorePanels) أو محرّر الإعدادات نفسه — المعالج ترتيبٌ وإرشاد، لا نسخة
// ثانية من القواعد. الخطوة في الرابط، فتحديث الصفحة أو العودة غداً يعيد المالك إلى حيث كان.
//
// "متابعة" لا تحفظ ضمنياً: الخطوة التي فيها تعديل غير محفوظ تمنع المتابعة وتقول لماذا — متابعةٌ تُسقط تعديلاً
// بصمت أسوأ من زرّ معطّل. وتخطّي المدير مسموح: الخادم لا يشترطه للتفعيل، والمراجعة تقول ما ينقص.
// ============================================================================
export default function StoreSetup() {
  const { t } = useTranslation();
  const { id, step } = useParams();
  const navigate = useNavigate();
  const options = useProvisioningOptions();
  const { store, accounts, refresh } = usePlatformStore(id);
  const [dirty, setDirty] = useState(false);

  if (!SETUP_STEPS.includes(step)) return <Navigate to={`/platform/stores/${id}/setup/${SETUP_STEPS[0]}`} replace />;
  const error = store.error ?? accounts.error ?? options.error;
  if (error) {
    return error.status === 404
      ? <ErrorBanner message={t('platform.store.notFound')} />
      : <ErrorBanner message={error.message} onRetry={() => { store.refetch(); accounts.refetch(); options.refetch(); }} />;
  }
  if (!store.data || !accounts.data || !options.data) return <Skeleton height={320} radius={14} />;

  const detail = store.data;
  const accountList = accounts.data.items;
  const go = (target) => { setDirty(false); navigate(`/platform/stores/${id}/setup/${target}`); };
  const next = nextStep(step);
  const previous = previousStep(step);

  const navigation = (
    <>
      {previous && <Button variant="ghost" type="button" onClick={() => go(previous)}>{t('platform.setup.back')}</Button>}
      {next && (
        <Button variant="accent" type="button" disabled={dirty} onClick={() => go(next)}
          title={dirty ? t('platform.setup.saveFirst') : undefined}>
          {step === 'admin' && accountList.every((a) => a.role !== 'TenantAdmin') ? t('platform.setup.skip') : t('platform.setup.continue')}
        </Button>
      )}
    </>
  );

  return (
    <div className={styles.setup}>
      <Link to="/platform/stores" className={styles.back}>{t('platform.setup.backToStores')}</Link>
      <div className={styles.pageHead}>
        <div>
          <h1 className={styles.title}>{t('platform.setup.title', { name: detail.name })}</h1>
          <p className={styles.subtitle}>
            <span dir="ltr">{detail.slug}</span> · <StatusBadge status={detail.status} />
          </p>
        </div>
        <Link to={`/platform/stores/${id}`} className={styles.secondaryLink}>{t('platform.setup.openStorePage')}</Link>
      </div>
      <SetupProgress current={step} storeId={id} />

      {step === 'branding' && (
        <>
          <p className={styles.panelHint}>{t('platform.setup.brandingHint')}</p>
          <PlatformStoreSettings store={detail} onSaved={refresh} onDirtyChange={setDirty} actions={navigation} />
        </>
      )}
      {step === 'domains' && <DomainsPanel store={detail} options={options.data} onChanged={refresh} />}
      {step === 'modules' && <ModulesPanel key={detail.modules.join()} store={detail} options={options.data} onChanged={refresh} />}
      {step === 'admin' && <AdminsPanel store={detail} accounts={accountList} options={options.data} onChanged={refresh} />}
      {step === 'review' && (
        <>
          <ReadinessPanel store={detail} accounts={accountList} />
          <LifecyclePanel store={detail} accounts={accountList} onChanged={refresh} />
          {detail.status !== 'Provisioning' && (
            <p className={styles.notice} role="status">
              {t('platform.setup.finished')} <Link to={`/platform/stores/${id}`}>{t('platform.setup.openStorePage')}</Link>
            </p>
          )}
        </>
      )}

      {step !== 'branding' && <div className={styles.stepNav}>{navigation}</div>}
    </div>
  );
}
