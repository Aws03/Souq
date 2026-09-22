import { useId, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../../api/client';
import { queryKeys } from '../../app/queryKeys';
import Button from '../../components/common/Button';
import Skeleton from '../../components/common/Skeleton';
import StatusBadge from '../../components/common/StatusBadge';
import { ErrorBanner } from '../../components/common/StateViews';
import { useToast } from '../../context/ToastContext';
import { statusTone } from '../../features/statusTone';
import { percentOf } from '../../features/platform/taxProfiles';
import { formatDate } from '../../i18n';
import admin from './Admin.module.css';
import styles from './Subscription.module.css';

// ============================================================================
// ضريبةُ المتجر كما يضبطها تاجره ([ADR-0055](0055)، قرار المالك P-06).
//
// **ولا نسبةَ تُدخَل هنا، ولن تكون.** ما يملكه المتجر ثلاثة: اختيارُ ملفّ اختصاص، وتفعيلُ
// الجمع، ورقمُ تسجيله هو. والنسبةُ قاعدةُ اختصاصٍ تحفظها المنصّة ويؤكّدها مهنيّ — ولو أدخل كلُّ
// متجرٍ نسبتَه لَما كان للتحقّق معنى، ولَعاد تشتُّتُ القيم الذي وُجد الملفّ ليُنهيه.
//
// وهذا هو موضعُ إعادة الاستخدام الذي طلبه قرار المالك بالنصّ: **متجرٌ ثانٍ في الاختصاص نفسه
// يختار الملفَّ الذي تحقّق منه محاسبُ الأوّل، ولا يُعيد إدخال شيء.**
//
// **وصفرُ الضريبة يُقال بسببه لا صمتاً.** أربعُ حالاتٍ لا تُجمَع فيها: لم يختر ملفّاً، أو اختار
// ولم يفعّل، أو فعّل ولا إصدارَ نافذ، أو الإصدارُ النافذ لم يؤكّده أحد. صفرٌ بلا سببٍ يُقرأ
// كأنّه عطب، فيُفتَح له بلاغٌ بدل أن يُكمَل إعداد.
// ============================================================================
export default function StoreTax() {
  const { t } = useTranslation();
  const toast = useToast();
  const queryClient = useQueryClient();
  const ids = { profile: useId(), registration: useId() };
  const [draft, setDraft] = useState(null);
  const [busy, setBusy] = useState(false);
  const [saveError, setSaveError] = useState(null);

  const settings = useQuery({ queryKey: queryKeys.storeTax(), queryFn: api.getStoreTax });
  const profiles = useQuery({ queryKey: queryKeys.storeTaxProfiles(), queryFn: api.getStoreTaxProfiles });

  if (settings.error) return <ErrorBanner message={settings.error.message} onRetry={settings.refetch} />;
  if (settings.isPending || !settings.data) return <Skeleton height={360} radius={14} />;

  const data = settings.data;
  // المسوّدةُ تبدأ من الخادم وتُستبدَل بما يحرّره التاجر — بلا أثرٍ يزامن الحالتين.
  const form = draft ?? {
    taxProfileId: data.taxProfileId ? String(data.taxProfileId) : '',
    collectionEnabled: Boolean(data.collectionEnabled),
    registrationNumber: data.registrationNumber ?? '',
  };
  const set = (patch) => { setSaveError(null); setDraft({ ...form, ...patch }); };

  const save = async (event) => {
    event.preventDefault();
    setBusy(true); setSaveError(null);
    try {
      await api.updateStoreTax({
        taxProfileId: form.taxProfileId ? Number(form.taxProfileId) : null,
        collectionEnabled: form.collectionEnabled,
        registrationNumber: form.registrationNumber.trim() || null,
      });
      setDraft(null);
      await queryClient.invalidateQueries({ queryKey: queryKeys.storeTax() });
      toast.success(t('admin.tax.saved'));
    } catch (err) {
      setSaveError(err.message);
    } finally {
      setBusy(false);
    }
  };

  const version = data.effectiveVersion;

  return (
    <div>
      <h1 className={admin.pageTitle}>{t('admin.tax.title')}</h1>
      <p className={admin.pageSub}>{t('admin.tax.subtitle')}</p>

      <section className={styles.panel} aria-labelledby="tax-state">
        <h2 id="tax-state" className={styles.panelTitle}>{t('admin.tax.state')}</h2>
        <p>
          <StatusBadge tone={data.collecting ? 'success' : 'warning'}>
            {t(data.collecting ? 'admin.tax.collecting' : 'admin.tax.notCollecting')}
          </StatusBadge>
        </p>
        {/* السببُ بالاسم، بالرموز نفسها التي يرسلها الخادم. */}
        <p className={styles.panelHint}>{t(`admin.tax.reason.${data.reason}`)}</p>

        {version && (
          <dl className={styles.facts}>
            <dt>{t('admin.tax.jurisdiction')}</dt>
            <dd><span dir="ltr">{data.jurisdiction}</span> — {data.profileName}</dd>
            <dt>{t('admin.tax.priceModeLabel')}</dt>
            <dd>{t(`admin.tax.priceMode.${version.priceMode}`)}</dd>
            <dt>{t('admin.tax.shipping')}</dt>
            <dd>{t(version.shippingTaxable ? 'admin.tax.shippingTaxed' : 'admin.tax.shippingNotTaxed')}</dd>
            <dt>{t('admin.tax.verification')}</dt>
            <dd>
              <StatusBadge tone={statusTone('taxVerification', version.verificationState)}>
                {t(`admin.tax.verificationState.${version.verificationState}`)}
              </StatusBadge>
              {version.verifiedBy && <> — {version.verifiedBy}
                {version.verifiedAt && <> ({formatDate(version.verifiedAt)})</>}</>}
            </dd>
            <dt>{t('admin.tax.rates')}</dt>
            <dd>
              {version.rates.map((rate) => (
                <div key={rate.code}>
                  {rate.name}: <span dir="ltr">{percentOf(rate.basisPoints)}%</span>
                </div>
              ))}
            </dd>
          </dl>
        )}
      </section>

      <form className={styles.panel} onSubmit={save} noValidate aria-labelledby="tax-form">
        <h2 id="tax-form" className={styles.panelTitle}>{t('admin.tax.settings')}</h2>
        {saveError && <ErrorBanner message={saveError} />}

        <div className={styles.facts}>
          <label htmlFor={ids.profile}>{t('admin.tax.profile')}</label>
          <div>
            {profiles.isPending
              ? <Skeleton height={42} radius={10} />
              : (
                <select id={ids.profile} className={admin.search} value={form.taxProfileId}
                  onChange={(e) => set({ taxProfileId: e.target.value })}>
                  <option value="">{t('admin.tax.noProfile')}</option>
                  {(profiles.data ?? []).map((profile) => (
                    <option key={profile.id} value={profile.id}>
                      {profile.jurisdiction} — {profile.name}
                      {profile.anyVersionAllowsCollection ? '' : ` (${t('admin.tax.unverifiedProfile')})`}
                    </option>
                  ))}
                </select>
              )}
            <p className={styles.panelHint}>{t('admin.tax.profileHint')}</p>
          </div>

          <label htmlFor={ids.registration}>{t('admin.tax.registrationNumber')}</label>
          <div>
            <input id={ids.registration} className={admin.search} dir="ltr" value={form.registrationNumber}
              maxLength={60} onChange={(e) => set({ registrationNumber: e.target.value })} />
            <p className={styles.panelHint}>{t('admin.tax.registrationNumberHint')}</p>
          </div>
        </div>

        <label>
          <input type="checkbox" checked={form.collectionEnabled} disabled={!form.taxProfileId}
            onChange={(e) => set({ collectionEnabled: e.target.checked })} />
          {' '}
          {t('admin.tax.collect')}
        </label>
        <p className={styles.panelHint}>{t('admin.tax.collectHint')}</p>

        <div style={{ marginTop: 'var(--space-3)' }}>
          <Button type="submit" variant="primary" loading={busy}>{t('common.save')}</Button>
        </div>
      </form>
    </div>
  );
}
