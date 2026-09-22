import { useId, useState } from 'react';
import { Link } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../../api/client';
import { queryKeys } from '../../app/queryKeys';
import Button from '../../components/common/Button';
import Skeleton from '../../components/common/Skeleton';
import StatusBadge from '../../components/common/StatusBadge';
import { ErrorBanner } from '../../components/common/StateViews';
import { useToast } from '../../context/ToastContext';
import styles from './Platform.module.css';

// ============================================================================
// إعدادُ فوترة المنصّة (C5، ADR-0056) — **وهنا يدخل جوابُ المالك `C-15` إلى المنتج**.
//
// السؤال كان: بأيّ عملةٍ تُفوتر سوق تجّارها؟ وللسؤال جوابٌ قرّره المالك — **ولا يُكتب ذلك الجواب
// في سطرٍ من الشيفرة، ولا يستطيع أن يُكتب**: قاعدةُ الواجهة البيضاء تمنع رمزَ عملةٍ في المصدر،
// ويحرسها اختبارٌ في الطرفين يقرأ التعليقاتِ كما يقرأ الشيفرة — وقد أمسك هذا التعليقَ نفسه حين
// سمّى العملة. فما بنته الهندسة آلةٌ تصحّ تحت أيّ عملة، **وهذه الشاشة هي المكان الذي يضع فيه
// المشغّل القيمة ويراها**، والجوابُ نفسه مكتوبٌ حيث تُكتب القرارات: `OwnerDecisions.md`.
//
// **ولا تُصدَر فاتورةٌ واحدة قبل أن تُملأ.** وما ينقص يُقال باسمه فوق النموذج، لا بزرٍّ معطَّل في
// شاشةٍ أخرى — مشغّلٌ يضغط «أصدِر» ولا يحدث شيء يفتح بلاغاً، ومشغّلٌ يقرأ «اضبط العملة أولاً»
// يُكمل إعداده.
//
// **والعملة تُقفَل بعد أوّل فاتورة صادرة**، ويقول الخادم ذلك برمزه الثابت: دفترٌ يقول عملةً
// وفواتيرُه تقول أخرى وضعٌ لا يُصلحه شيء.
// ============================================================================
export default function BillingSettings() {
  const ids = {
    currency: useId(), issuerName: useId(), issuerAddress: useId(), issuerTaxNumber: useId(),
    invoicePrefix: useId(), creditPrefix: useId(), terms: useId(), grace: useId(),
    instructions: useId(), taxProfile: useId(),
  };

  const { data, error, isPending, refetch } = useQuery({
    queryKey: queryKeys.platformBillingSettings(),
    queryFn: api.getPlatformBillingSettings,
  });

  // ملفّاتُ الاختصاص التي تستطيع المنصّة أن تُفوتر تحتها. تُقرأ من نقطة الضريبة نفسها التي
  // يقرأ منها التاجر — قائمةٌ واحدة، لا نسخةٌ ثانية في هذه الشاشة.
  const profiles = useQuery({
    queryKey: queryKeys.platformTaxProfiles(),
    queryFn: () => api.getPlatformTaxProfiles(),
  });

  if (error) return <ErrorBanner message={error.message} onRetry={refetch} />;
  if (isPending || !data) return <Skeleton height={480} radius={14} />;

  // النموذجُ يُهيّأ من البيانات **مرّةً عند تركيبه**، لا في أثرٍ يعمل بعد الرسم: `key` تُعيد
  // تركيبه لو تغيّر الصفّ من الخادم. والبديل — `useEffect` يستدعي `setForm` — يُنتج رسمةً
  // إضافية ويشكو منه الـ lint بحقّ.
  return (
    <BillingSettingsForm
      key={`${data.currency ?? ''}|${data.issuerName ?? ''}`}
      settings={data}
      profiles={profiles.data ?? []}
      ids={ids}
    />
  );
}

function BillingSettingsForm({ settings: data, profiles, ids }) {
  const { t } = useTranslation();
  const toast = useToast();
  const queryClient = useQueryClient();
  const [form, setForm] = useState(() => ({
    currency: data.currency ?? '',
    issuerName: data.issuerName ?? '',
    issuerAddress: data.issuerAddress ?? '',
    issuerTaxNumber: data.issuerTaxNumber ?? '',
    invoiceNumberPrefix: data.invoiceNumberPrefix ?? '',
    creditNoteNumberPrefix: data.creditNoteNumberPrefix ?? '',
    paymentTermsDays: String(data.paymentTermsDays ?? 30),
    gracePeriodDays: String(data.gracePeriodDays ?? 7),
    paymentInstructions: data.paymentInstructions ?? '',
    taxProfileId: data.taxProfileId ? String(data.taxProfileId) : '',
    taxCollectionEnabled: Boolean(data.taxCollectionEnabled),
  }));
  const [busy, setBusy] = useState(false);
  const [saveError, setSaveError] = useState(null);

  const set = (field) => (e) => {
    const value = e.target.type === 'checkbox' ? e.target.checked : e.target.value;
    setSaveError(null);
    setForm((f) => ({ ...f, [field]: value }));
  };

  const save = async (event) => {
    event.preventDefault();
    setBusy(true); setSaveError(null);
    try {
      await api.updatePlatformBillingSettings({
        currency: form.currency.trim() || null,
        issuerName: form.issuerName.trim() || null,
        issuerAddress: form.issuerAddress.trim() || null,
        issuerTaxNumber: form.issuerTaxNumber.trim() || null,
        invoiceNumberPrefix: form.invoiceNumberPrefix.trim() || null,
        creditNoteNumberPrefix: form.creditNoteNumberPrefix.trim() || null,
        paymentTermsDays: Number(form.paymentTermsDays),
        gracePeriodDays: Number(form.gracePeriodDays),
        paymentInstructions: form.paymentInstructions.trim() || null,
        taxProfileId: form.taxProfileId ? Number(form.taxProfileId) : null,
        taxCollectionEnabled: form.taxCollectionEnabled,
      });
      await queryClient.invalidateQueries({ queryKey: queryKeys.platformBillingSettings() });
      await queryClient.invalidateQueries({ queryKey: queryKeys.platformInvoicesAll() });
      toast.success(t('platform.billing.saved'));
    } catch (err) {
      setSaveError(err.message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <div>
      <div className={styles.pageHead}>
        <div>
          <h1 className={styles.title}>{t('platform.billing.title')}</h1>
          <p className={styles.subtitle}>{t('platform.billing.subtitle')}</p>
        </div>
        <Link to="/platform/invoices" className={styles.secondaryLink}>{t('platform.billing.invoicesLink')}</Link>
      </div>

      {/* الحالةُ أوّلاً: هل تستطيع المنصّة أن تُصدر اليوم، وإن لم تستطع فلماذا. */}
      <section className={styles.panel} aria-labelledby="billing-readiness">
        <h2 id="billing-readiness" className={styles.panelTitle}>{t('platform.billing.readiness')}</h2>
        <p>
          <StatusBadge tone={data.canIssue ? 'success' : 'warning'}>
            {t(data.canIssue ? 'platform.billing.ready' : 'platform.billing.notReady')}
          </StatusBadge>
        </p>
        {!data.canIssue && (
          <p className={styles.panelHint}>
            {t(`platform.billing.blocked.${data.blockingReason ?? 'BillingSettingsMissing'}`)}
          </p>
        )}
        {/* سببُ عدم جمع الضريبة على الفواتير — بالمفردات نفسها التي يقرؤها التاجر عن متجره. */}
        <p className={styles.panelHint}>{t(`platform.billing.taxReason.${data.taxReason}`)}</p>
      </section>

      <form className={styles.panel} onSubmit={save} noValidate aria-labelledby="billing-form">
        <h2 id="billing-form" className={styles.panelTitle}>{t('platform.billing.settings')}</h2>
        {saveError && <ErrorBanner message={saveError} />}

        <div className={styles.formGrid}>
          <div className={styles.field}>
            <label htmlFor={ids.currency} className={styles.label}>{t('platform.billing.currency')}</label>
            <input
              id={ids.currency}
              className={`${styles.input} ${styles.mono}`}
              dir="ltr"
              value={form.currency}
              maxLength={3}
              autoComplete="off"
              spellCheck={false}
              onChange={(e) => set('currency')({ target: { value: e.target.value.toUpperCase() } })}
            />
            <span className={styles.hint}>{t('platform.billing.currencyHint')}</span>
          </div>

          <div className={styles.field}>
            <label htmlFor={ids.issuerName} className={styles.label}>{t('platform.billing.issuerName')}</label>
            <input id={ids.issuerName} className={styles.input} value={form.issuerName} maxLength={200}
              onChange={set('issuerName')} />
            <span className={styles.hint}>{t('platform.billing.issuerNameHint')}</span>
          </div>

          <div className={styles.field}>
            <label htmlFor={ids.issuerTaxNumber} className={styles.label}>{t('platform.billing.issuerTaxNumber')}</label>
            <input id={ids.issuerTaxNumber} className={`${styles.input} ${styles.mono}`} dir="ltr"
              value={form.issuerTaxNumber} maxLength={60} onChange={set('issuerTaxNumber')} />
          </div>

          <div className={styles.field}>
            <label htmlFor={ids.issuerAddress} className={styles.label}>{t('platform.billing.issuerAddress')}</label>
            <textarea id={ids.issuerAddress} className={styles.input} rows={2} value={form.issuerAddress}
              maxLength={500} onChange={set('issuerAddress')} />
          </div>

          <div className={styles.field}>
            <label htmlFor={ids.invoicePrefix} className={styles.label}>{t('platform.billing.invoicePrefix')}</label>
            <input id={ids.invoicePrefix} className={`${styles.input} ${styles.mono}`} dir="ltr"
              value={form.invoiceNumberPrefix} maxLength={10}
              onChange={(e) => set('invoiceNumberPrefix')({ target: { value: e.target.value.toUpperCase() } })} />
            <span className={styles.hint}>{t('platform.billing.prefixHint')}</span>
          </div>

          <div className={styles.field}>
            <label htmlFor={ids.creditPrefix} className={styles.label}>{t('platform.billing.creditPrefix')}</label>
            <input id={ids.creditPrefix} className={`${styles.input} ${styles.mono}`} dir="ltr"
              value={form.creditNoteNumberPrefix} maxLength={10}
              onChange={(e) => set('creditNoteNumberPrefix')({ target: { value: e.target.value.toUpperCase() } })} />
          </div>

          <div className={styles.field}>
            <label htmlFor={ids.terms} className={styles.label}>{t('platform.billing.paymentTerms')}</label>
            <input id={ids.terms} className={`${styles.input} ${styles.mono}`} dir="ltr" type="number" min="0" max="365"
              value={form.paymentTermsDays} onChange={set('paymentTermsDays')} />
            <span className={styles.hint}>{t('platform.billing.paymentTermsHint')}</span>
          </div>

          <div className={styles.field}>
            <label htmlFor={ids.grace} className={styles.label}>{t('platform.billing.gracePeriod')}</label>
            <input id={ids.grace} className={`${styles.input} ${styles.mono}`} dir="ltr" type="number" min="0" max="365"
              value={form.gracePeriodDays} onChange={set('gracePeriodDays')} />
            {/* تُقرأ ولا يُتصرَّف بها بعد: المطالبةُ الآلية هي C6، وهذه قيمتُها المدخلة. */}
            <span className={styles.hint}>{t('platform.billing.gracePeriodHint')}</span>
          </div>

          <div className={styles.field}>
            <label htmlFor={ids.instructions} className={styles.label}>{t('platform.billing.instructions')}</label>
            <textarea id={ids.instructions} className={styles.input} rows={3} value={form.paymentInstructions}
              maxLength={2000} onChange={set('paymentInstructions')} />
            <span className={styles.hint}>{t('platform.billing.instructionsHint')}</span>
          </div>

          <div className={styles.field}>
            <label htmlFor={ids.taxProfile} className={styles.label}>{t('platform.billing.taxProfile')}</label>
            <select id={ids.taxProfile} className={styles.input} value={form.taxProfileId} onChange={set('taxProfileId')}>
              <option value="">{t('platform.billing.noTaxProfile')}</option>
              {profiles.map((profile) => (
                <option key={profile.id} value={profile.id}>
                  {profile.jurisdiction} — {profile.name}
                </option>
              ))}
            </select>
            <span className={styles.hint}>{t('platform.billing.taxProfileHint')}</span>
          </div>
        </div>

        <label className={styles.option}>
          <input type="checkbox" checked={form.taxCollectionEnabled} onChange={set('taxCollectionEnabled')}
            disabled={!form.taxProfileId} />
          <span>
            {t('platform.billing.collectTax')}
            <span className={styles.hint}> {t('platform.billing.collectTaxHint')}</span>
          </span>
        </label>

        <div className={styles.formActions}>
          <Button type="submit" variant="primary" loading={busy}>{t('common.save')}</Button>
        </div>
      </form>
    </div>
  );
}
