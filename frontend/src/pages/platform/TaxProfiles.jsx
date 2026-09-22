import { useId, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../../api/client';
import { queryKeys } from '../../app/queryKeys';
import Button from '../../components/common/Button';
import Skeleton from '../../components/common/Skeleton';
import StatusBadge from '../../components/common/StatusBadge';
import { EmptyState, ErrorBanner } from '../../components/common/StateViews';
import { useConfirmAction } from '../../components/common/useConfirmAction';
import { useToast } from '../../context/ToastContext';
import { statusTone } from '../../features/statusTone';
import { formatDate } from '../../i18n';
import {
  PRICE_MODES, basisPointsOf, draftVersion, effectiveVersion, percentOf, profileCollects,
  rateProblems, versionProblems, versionsNewestFirst,
} from '../../features/platform/taxProfiles';
import styles from './Platform.module.css';

// ============================================================================
// ملفّاتُ الاختصاص الضريبيّ من المنصّة ([ADR-0055](0055)، قرار المالك P-06).
//
// **الشاشةُ أُضيفت لأنّ القدرة كانت بلا واجهةٍ إطلاقاً**: شُحنت وحدةُ الضريبة كاملةً بعشر نقاط
// API ولا شاشةَ واحدة تلمسها — فلا مشغّلٌ يستطيع إدخال قواعد اختصاص، ولا تاجرٌ يستطيع اختيارها.
// قدرةٌ لا يصلها أحد من لوحته ليست قدرةً في منتج.
//
// ============================================================================
// **وثلاثةُ أشياء في هذه الشاشة ليست تفصيلاً في العرض، بل هي القرار نفسه:**
//
//   1. **النسبة تُدخَل بالمئة وتُحفَظ بنقاط الأساس.** الشاشةُ تُظهر 16 ويُحفَظ 1600، فلا تقترب
//      حسابات الضريبة من الفاصلة العائمة من أيّ جهة.
//   2. **«يُضرَّب الشحن» سؤالٌ بلا افتراض.** لا قيمةَ مبدئية له في النموذج ولا في المجال: مَن
//      يُدخل قواعد الاختصاص يُجيب، وافتراضُ أحدهما خطأٌ في كل طلب لا يظهر إلّا في تسوية.
//   3. **«تحقّقتُ» ليست خانةَ تأشير.** هي فعلٌ منفصل يُسمّي فاعلَه ويُدقَّق، ولا تُقبَل على
//      مسوّدة — والهندسةُ لا تضعها أبداً. فما تراه هذه الشاشة من أرقامٍ يبقى عاجزاً عن أن يصل
//      مشترياً حتى يؤكّده مهنيٌّ باسمه.
// ============================================================================
export default function TaxProfiles() {
  const { t } = useTranslation();
  const [openId, setOpenId] = useState(null);

  const { data, error, isPending, refetch } = useQuery({
    queryKey: queryKeys.platformTaxProfiles(),
    queryFn: api.getPlatformTaxProfiles,
  });

  if (error) return <ErrorBanner message={error.message} onRetry={refetch} />;

  return (
    <div>
      <div className={styles.pageHead}>
        <div>
          <h1 className={styles.title}>{t('platform.tax.title')}</h1>
          <p className={styles.subtitle}>{t('platform.tax.subtitle')}</p>
        </div>
      </div>

      {/* القاعدةُ التي تحكم كلَّ ما تحت هذا السطر، مكتوبةً حيث تُقرأ لا في وثيقة. */}
      <p className={styles.notice} role="note">{t('platform.tax.disclaimer')}</p>

      <NewProfileForm />

      {isPending && <Skeleton height={220} radius={14} />}

      {data?.length === 0 && (
        <EmptyState title={t('platform.tax.emptyTitle')} message={t('platform.tax.emptyMessage')} />
      )}

      {(data ?? []).map((profile) => (
        <ProfileCard
          key={profile.id}
          profile={profile}
          open={openId === profile.id}
          onToggle={() => setOpenId(openId === profile.id ? null : profile.id)}
        />
      ))}
    </div>
  );
}

// ملفٌّ لاختصاص، بلا إصدارٍ بعد. ملفٌّ ثانٍ للاختصاص نفسه يرفضه الخادم: التصحيحُ إصدار لا ملفّ.
function NewProfileForm() {
  const { t } = useTranslation();
  const toast = useToast();
  const queryClient = useQueryClient();
  const ids = { jurisdiction: useId(), name: useId() };
  const [form, setForm] = useState({ jurisdiction: '', name: '' });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);

  const create = async (event) => {
    event.preventDefault();
    setBusy(true); setError(null);
    try {
      await api.createPlatformTaxProfile({
        jurisdiction: form.jurisdiction.trim(),
        name: form.name.trim(),
      });
      setForm({ jurisdiction: '', name: '' });
      await queryClient.invalidateQueries({ queryKey: queryKeys.platformTaxProfiles() });
      toast.success(t('platform.tax.profileCreated'));
    } catch (err) {
      setError(err.message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <form className={styles.panel} onSubmit={create} noValidate aria-labelledby="new-tax-profile">
      <h2 id="new-tax-profile" className={styles.panelTitle}>{t('platform.tax.newProfile')}</h2>
      {error && <ErrorBanner message={error} />}

      <div className={styles.formGrid}>
        <div className={styles.field}>
          <label htmlFor={ids.jurisdiction} className={styles.label}>{t('platform.tax.jurisdiction')}</label>
          <input
            id={ids.jurisdiction}
            className={`${styles.input} ${styles.mono}`}
            dir="ltr"
            value={form.jurisdiction}
            maxLength={10}
            spellCheck={false}
            onChange={(e) => setForm((f) => ({ ...f, jurisdiction: e.target.value.toUpperCase() }))}
          />
          <span className={styles.hint}>{t('platform.tax.jurisdictionHint')}</span>
        </div>

        <div className={styles.field}>
          <label htmlFor={ids.name} className={styles.label}>{t('platform.tax.profileName')}</label>
          <input id={ids.name} className={styles.input} value={form.name} maxLength={120}
            onChange={(e) => setForm((f) => ({ ...f, name: e.target.value }))} />
        </div>
      </div>

      <div className={styles.formActions}>
        <Button type="submit" variant="secondary" loading={busy}
          disabled={!form.jurisdiction.trim() || !form.name.trim()}>
          {t('platform.tax.createProfile')}
        </Button>
      </div>
    </form>
  );
}

function ProfileCard({ profile, open, onToggle }) {
  const { t } = useTranslation();
  const collects = profileCollects(profile);
  const current = effectiveVersion(profile);

  return (
    <section className={styles.panel} aria-labelledby={`tax-profile-${profile.id}`}>
      <div className={styles.pageHead}>
        <div>
          <h2 id={`tax-profile-${profile.id}`} className={styles.panelTitle}>
            <span dir="ltr">{profile.jurisdiction}</span> — {profile.name}
          </h2>
          <p className={styles.panelHint}>
            {current
              ? t('platform.tax.currentVersion', {
                version: current.version,
                percent: percentOf(current.rates.reduce((sum, r) => sum + r.basisPoints, 0)),
              })
              : t('platform.tax.noEffectiveVersion')}
          </p>
        </div>
        <StatusBadge tone={collects ? 'success' : 'warning'}>
          {t(collects ? 'platform.tax.collects' : 'platform.tax.doesNotCollect')}
        </StatusBadge>
      </div>

      <div className={styles.formActions}>
        <Button variant="ghost" onClick={onToggle} aria-expanded={open}>
          {t(open ? 'platform.tax.hideVersions' : 'platform.tax.showVersions')}
        </Button>
      </div>

      {open && <VersionsPanel profile={profile} />}
    </section>
  );
}

function VersionsPanel({ profile }) {
  const { t } = useTranslation();
  const toast = useToast();
  const queryClient = useQueryClient();
  const confirmation = useConfirmAction();
  const versions = versionsNewestFirst(profile);
  const hasDraft = Boolean(draftVersion(profile));

  const reload = () => queryClient.invalidateQueries({ queryKey: queryKeys.platformTaxProfiles() });

  const publish = (version) => confirmation.ask({
    title: t('platform.tax.confirmPublish.title'),
    message: t('platform.tax.confirmPublish.message'),
    confirmLabel: t('platform.tax.publish'),
    action: async () => {
      await api.publishPlatformTaxVersion(profile.id, version.id);
      await reload();
      toast.success(t('platform.tax.published'));
    },
  });

  const withdraw = (version) => confirmation.ask({
    title: t('platform.tax.confirmWithdraw.title'),
    message: t('platform.tax.confirmWithdraw.message'),
    confirmLabel: t('platform.tax.withdraw'),
    danger: true,
    action: async () => {
      await api.requirePlatformTaxConfirmation(profile.id, version.id, { note: null });
      await reload();
      toast.success(t('platform.tax.withdrawn'));
    },
  });

  return (
    <div>
      <ul className={styles.rows}>
        {versions.map((version) => (
          <li key={version.id} className={styles.row}>
            <span className={styles.rowMain}>
              {t('platform.tax.versionLabel', { version: version.version })}
              {' · '}
              {formatDate(version.effectiveFrom)}
            </span>
            <span className={styles.rowTags}>
              <StatusBadge tone={statusTone('taxVersion', version.status)}>
                {t(`platform.tax.versionStatus.${version.status}`)}
              </StatusBadge>
              {' '}
              {/* حالةُ التحقّق تظهر حيث يظهر الرقم — وهي الفرق بين قيمةٍ في القاعدة وقيمةٍ
                  يُعتمد عليها. */}
              <StatusBadge tone={statusTone('taxVerification', version.verificationState)}>
                {t(`platform.tax.verification.${version.verificationState}`)}
              </StatusBadge>
              {' · '}
              {t(`platform.tax.priceMode.${version.priceMode}`)}
              {' · '}
              {t(version.shippingTaxable ? 'platform.tax.shippingTaxed' : 'platform.tax.shippingNotTaxed')}
            </span>

            <ul className={styles.rows}>
              {version.rates.map((rate) => (
                <li key={rate.code} className={styles.row}>
                  <span className={styles.rowMain}>{rate.name}</span>
                  <span className={styles.rowTags}>
                    <span dir="ltr">{percentOf(rate.basisPoints)}%</span>
                    {' · '}
                    <span dir="ltr" className={styles.mono}>{rate.basisPoints} bp</span>
                  </span>
                </li>
              ))}
            </ul>

            {version.verifiedBy && (
              <p className={styles.panelHint}>
                {t('platform.tax.verifiedBy', {
                  by: version.verifiedBy,
                  at: version.verifiedAt ? formatDate(version.verifiedAt) : '',
                })}
              </p>
            )}

            <div className={styles.formActions}>
              {version.status === 'Draft' && (
                <Button variant="secondary" onClick={() => publish(version)}>{t('platform.tax.publish')}</Button>
              )}
              {version.status === 'Published' && version.verificationState !== 'Verified' && (
                <VerifyForm profile={profile} version={version} onDone={reload} />
              )}
              {version.status === 'Published' && version.verificationState === 'Verified' && (
                <Button variant="danger" onClick={() => withdraw(version)}>{t('platform.tax.withdraw')}</Button>
              )}
            </div>
          </li>
        ))}
      </ul>

      {hasDraft
        ? <p className={styles.panelHint}>{t('platform.tax.draftExists')}</p>
        : <NewVersionForm profile={profile} onDone={reload} />}

      {confirmation.dialog}
    </div>
  );
}

// ============================================================================
// تسجيلُ التحقّق. **ليست خانةَ تأشير**: الجسمُ يحمل مَن تحقّق باسمه، والفعلُ مُدقَّق.
// وهذا ليس إقراراً من النظام بصحّة الأرقام، بل تسجيلٌ لمن أقرّها — والفرقُ بينهما هو كلُّ ما
// يعنيه قرارُ المالك بأنّ قيمَ الملفّ «إعدادٌ يحتاج تحقّقاً مهنياً قبل الاستخدام التجاري».
// ============================================================================
function VerifyForm({ profile, version, onDone }) {
  const { t } = useTranslation();
  const toast = useToast();
  const ids = { by: useId(), note: useId() };
  const [open, setOpen] = useState(false);
  const [form, setForm] = useState({ verifiedBy: '', note: '' });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);

  if (!open) {
    return <Button variant="primary" onClick={() => setOpen(true)}>{t('platform.tax.recordVerification')}</Button>;
  }

  const submit = async (event) => {
    event.preventDefault();
    setBusy(true); setError(null);
    try {
      await api.verifyPlatformTaxVersion(profile.id, version.id, {
        verifiedBy: form.verifiedBy.trim(),
        note: form.note.trim() || null,
      });
      setOpen(false);
      setForm({ verifiedBy: '', note: '' });
      await onDone();
      toast.success(t('platform.tax.verified'));
    } catch (err) {
      setError(err.message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <form onSubmit={submit} noValidate>
      {error && <ErrorBanner message={error} />}
      <p className={styles.panelHint}>{t('platform.tax.verifyHint')}</p>
      <div className={styles.formGrid}>
        <div className={styles.field}>
          <label htmlFor={ids.by} className={styles.label}>{t('platform.tax.verifiedByLabel')}</label>
          <input id={ids.by} className={styles.input} value={form.verifiedBy} maxLength={120} required
            onChange={(e) => setForm((f) => ({ ...f, verifiedBy: e.target.value }))} />
          <span className={styles.hint}>{t('platform.tax.verifiedByHint')}</span>
        </div>
        <div className={styles.field}>
          <label htmlFor={ids.note} className={styles.label}>{t('platform.tax.verificationNote')}</label>
          <input id={ids.note} className={styles.input} value={form.note} maxLength={500}
            onChange={(e) => setForm((f) => ({ ...f, note: e.target.value }))} />
        </div>
      </div>
      <div className={styles.formActions}>
        <Button variant="secondary" onClick={() => setOpen(false)}>{t('common.cancel')}</Button>
        <Button type="submit" variant="primary" loading={busy} disabled={!form.verifiedBy.trim()}>
          {t('platform.tax.recordVerification')}
        </Button>
      </div>
    </form>
  );
}

// مسوّدةُ إصدارٍ جديد بقيمها. تبدأ **غيرَ متحقَّقٍ منها** دائماً، ولا مدخلَ هنا يغيّر ذلك.
function NewVersionForm({ profile, onDone }) {
  const { t } = useTranslation();
  const toast = useToast();
  const ids = { from: useId(), mode: useId(), shipping: useId(), code: useId(), name: useId(), percent: useId() };
  const [version, setVersion] = useState({
    effectiveFrom: new Date().toISOString().slice(0, 10),
    priceMode: '',
    // بلا افتراض: قاعدةُ اختصاصٍ يجيب عنها مَن يُدخل القواعد.
    shippingTaxable: null,
    rates: [],
  });
  const [rate, setRate] = useState({ code: '', name: '', percent: '' });
  const [submitted, setSubmitted] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);

  const problems = submitted ? versionProblems(version) : {};
  const rateIssues = rateProblems(rate);

  const addRate = () => {
    if (Object.keys(rateIssues).length > 0) return;
    setVersion((v) => ({
      ...v,
      rates: [...v.rates, { code: rate.code.trim(), name: rate.name.trim(), percent: Number(rate.percent) }],
    }));
    setRate({ code: '', name: '', percent: '' });
  };

  const submit = async (event) => {
    event.preventDefault();
    setSubmitted(true);
    if (Object.keys(versionProblems(version)).length > 0) return;
    setBusy(true); setError(null);
    try {
      await api.addPlatformTaxVersion(profile.id, {
        effectiveFrom: new Date(`${version.effectiveFrom}T00:00:00Z`).toISOString(),
        priceMode: version.priceMode,
        shippingTaxable: version.shippingTaxable,
        rates: version.rates.map((r) => ({
          code: r.code, name: r.name, basisPoints: basisPointsOf(r.percent), category: null,
        })),
        registrationThreshold: null,
        notes: null,
      });
      setVersion({ effectiveFrom: new Date().toISOString().slice(0, 10), priceMode: '', shippingTaxable: null, rates: [] });
      setSubmitted(false);
      await onDone();
      toast.success(t('platform.tax.versionDrafted'));
    } catch (err) {
      setError(err.message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <form onSubmit={submit} noValidate aria-labelledby={`new-version-${profile.id}`}>
      <h3 id={`new-version-${profile.id}`} className={styles.panelTitle}>{t('platform.tax.newVersion')}</h3>
      {error && <ErrorBanner message={error} />}

      <div className={styles.formGrid}>
        <div className={styles.field}>
          <label htmlFor={ids.from} className={styles.label}>{t('platform.tax.effectiveFrom')}</label>
          <input id={ids.from} className={styles.input} type="date" value={version.effectiveFrom}
            onChange={(e) => setVersion((v) => ({ ...v, effectiveFrom: e.target.value }))} />
          <span className={styles.hint}>{t('platform.tax.effectiveFromHint')}</span>
        </div>

        <div className={styles.field}>
          <label htmlFor={ids.mode} className={styles.label}>{t('platform.tax.priceModeLabel')}</label>
          <select id={ids.mode} className={styles.input} value={version.priceMode}
            aria-invalid={!!problems.priceMode}
            onChange={(e) => setVersion((v) => ({ ...v, priceMode: e.target.value }))}>
            <option value="">{t('platform.tax.choose')}</option>
            {PRICE_MODES.map((mode) => (
              <option key={mode} value={mode}>{t(`platform.tax.priceMode.${mode}`)}</option>
            ))}
          </select>
          <span className={problems.priceMode ? styles.fieldError : styles.hint}>
            {problems.priceMode ? t('platform.tax.problem.required') : t('platform.tax.priceModeHint')}
          </span>
        </div>

        <div className={styles.field}>
          {/* ثلاثُ حالات لا اثنتان: «نعم» و«لا» و**«لم يُجَب»** — وخانةُ تأشيرٍ لا تستطيع
              التعبير عن الثالثة، فتفرض افتراضاً هو بالضبط ما يمنعه المجال. */}
          <label htmlFor={ids.shipping} className={styles.label}>{t('platform.tax.shippingTaxable')}</label>
          <select id={ids.shipping} className={styles.input}
            value={version.shippingTaxable === null ? '' : String(version.shippingTaxable)}
            aria-invalid={!!problems.shippingTaxable}
            onChange={(e) => setVersion((v) => ({
              ...v, shippingTaxable: e.target.value === '' ? null : e.target.value === 'true',
            }))}>
            <option value="">{t('platform.tax.choose')}</option>
            <option value="true">{t('platform.tax.shippingTaxed')}</option>
            <option value="false">{t('platform.tax.shippingNotTaxed')}</option>
          </select>
          <span className={problems.shippingTaxable ? styles.fieldError : styles.hint}>
            {problems.shippingTaxable ? t('platform.tax.problem.required') : t('platform.tax.shippingTaxableHint')}
          </span>
        </div>
      </div>

      {version.rates.length > 0 && (
        <ul className={styles.rows}>
          {version.rates.map((r) => (
            <li key={r.code} className={styles.row}>
              <span className={styles.rowMain}>{r.name} (<span dir="ltr">{r.code}</span>)</span>
              <span className={styles.rowTags}>
                <span dir="ltr">{r.percent}%</span> · <span dir="ltr" className={styles.mono}>{basisPointsOf(r.percent)} bp</span>
              </span>
              <Button variant="ghost" size="sm"
                onClick={() => setVersion((v) => ({ ...v, rates: v.rates.filter((x) => x.code !== r.code) }))}>
                {t('common.delete')}
              </Button>
            </li>
          ))}
        </ul>
      )}

      <div className={styles.formGrid}>
        <div className={styles.field}>
          <label htmlFor={ids.code} className={styles.label}>{t('platform.tax.rateCode')}</label>
          <input id={ids.code} className={`${styles.input} ${styles.mono}`} dir="ltr" value={rate.code}
            maxLength={40} onChange={(e) => setRate((r) => ({ ...r, code: e.target.value }))} />
        </div>
        <div className={styles.field}>
          <label htmlFor={ids.name} className={styles.label}>{t('platform.tax.rateName')}</label>
          <input id={ids.name} className={styles.input} value={rate.name} maxLength={120}
            onChange={(e) => setRate((r) => ({ ...r, name: e.target.value }))} />
        </div>
        <div className={styles.field}>
          {/* بالمئة يُدخَل، وبنقاط الأساس يُحفَظ — والتحويلُ في موضعٍ واحد مُختبَر. */}
          <label htmlFor={ids.percent} className={styles.label}>{t('platform.tax.ratePercent')}</label>
          <input id={ids.percent} className={`${styles.input} ${styles.mono}`} dir="ltr" type="number"
            step="0.01" min="0" max="100" value={rate.percent}
            onChange={(e) => setRate((r) => ({ ...r, percent: e.target.value }))} />
          <span className={styles.hint}>
            {rate.percent === '' || rateIssues.percent
              ? t('platform.tax.ratePercentHint')
              : t('platform.tax.basisPointsPreview', { points: basisPointsOf(rate.percent) })}
          </span>
        </div>
      </div>

      <div className={styles.formActions}>
        <Button variant="ghost" onClick={addRate} disabled={Object.keys(rateIssues).length > 0}>
          {t('platform.tax.addRate')}
        </Button>
      </div>

      {problems.rates && <p className={styles.fieldError}>{t('platform.tax.problem.atLeastOneRate')}</p>}

      <p className={styles.notice} role="note">{t('platform.tax.draftStartsUnverified')}</p>

      <div className={styles.formActions}>
        <Button type="submit" variant="secondary" loading={busy}>{t('platform.tax.createVersion')}</Button>
      </div>
    </form>
  );
}
