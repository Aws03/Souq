import { useId, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useQueryClient } from '@tanstack/react-query';
import { api } from '../../api/client';
import Button from '../../components/common/Button';
import Skeleton from '../../components/common/Skeleton';
import { ErrorBanner } from '../../components/common/StateViews';
import { TimeZones } from '../../components/settings/StoreSettingsEditor';
import {
  SETUP_STEPS, buildIdentityPayload, identityProblems, identityToForm, slugFromName,
} from '../../features/platform/provisioning';
import { useProvisioningOptions } from '../../features/platform/usePlatformStore';
import SetupProgress from './SetupProgress';
import styles from './Platform.module.css';

// ============================================================================
// الخطوة الأولى في التجهيز: هوية المتجر — الاسم والمعرّف والعملة واللغة والمنطقة الزمنية.
//
// هي الخطوة الوحيدة التي تُنشئ صفّاً. المتجر يولد "قيد التجهيز": مغلقاً للزوّار (503 StoreUnavailable) بلا نطاق
// ولا مدير. فمعالجٌ يُترك في منتصفه لا يترك متجراً مفتوحاً نصف مُعدّ — يترك متجراً مغلقاً يظهر في القائمة بـ
// "متابعة التجهيز". لا معاملة تمتدّ عبر الشاشات، ولا "تراجع" يحذف شيئاً.
//
// المعرّف ثابت بعد الإنشاء (جزء من المضيف الفرعي والروابط)، والعملة تُقفل بعد أوّل نشاط تجاري — فيُقال ذلك هنا.
// ============================================================================
export default function NewStore() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const options = useProvisioningOptions();
  const [form, setForm] = useState(identityToForm);
  const [slugTouched, setSlugTouched] = useState(false);
  const [submitted, setSubmitted] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);
  const [serverProblems, setServerProblems] = useState({});
  const ids = { name: useId(), slug: useId(), currency: useId(), culture: useId(), zone: useId() };

  if (options.error) return <ErrorBanner message={options.error.message} onRetry={options.refetch} />;
  if (!options.data) return <Skeleton height={320} radius={14} />;

  const limits = options.data.limits;
  const problems = submitted ? { ...identityProblems(form, options.data), ...serverProblems } : serverProblems;
  const message = (field) => (problems[field] ? t(`platform.problem.${problems[field].key}`, problems[field].values) : null);

  const setName = (name) => setForm((f) => ({ ...f, name, slug: slugTouched ? f.slug : slugFromName(name, limits.slugMax) }));
  const set = (field) => (e) => {
    const { value } = e.target;
    // الحذف لا "undefined": مفتاحٌ بقيمة undefined يُنشر فوق فحص الحقل فيمحو مشكلته هو أيضاً.
    setServerProblems(({ [field]: _cleared, ...rest }) => rest);
    setForm((f) => ({ ...f, [field]: value }));
  };

  const create = async (event) => {
    event.preventDefault();
    setSubmitted(true);
    const found = identityProblems(form, options.data);
    if (Object.keys(found).length > 0) {
      document.getElementById(ids[{ name: 'name', slug: 'slug', currency: 'currency', defaultCulture: 'culture', timeZone: 'zone' }[Object.keys(found)[0]]])?.focus();
      return;
    }
    setBusy(true); setError(null);
    try {
      const { id } = await api.createPlatformStore(buildIdentityPayload(form));
      queryClient.invalidateQueries({ queryKey: ['platform-stores'] });
      navigate(`/platform/stores/${id}/setup/${SETUP_STEPS[0]}`, { replace: true });
    } catch (err) {
      // معرّف مأخوذ مشكلة حقل لا الصفحة: يُقال بجانب المعرّف، والتركيز يعود إليه.
      if (err.code === 'TenantSlugTaken') {
        setServerProblems({ slug: { key: 'slugTaken' } });
        document.getElementById(ids.slug)?.focus();
      } else {
        setError(err.message);
      }
      setBusy(false);
    }
  };

  return (
    <div className={styles.setup}>
      <Link to="/platform/stores" className={styles.back}>{t('platform.setup.backToStores')}</Link>
      <h1 className={styles.title}>{t('platform.setup.newTitle')}</h1>
      <p className={styles.subtitle}>{t('platform.setup.newSubtitle')}</p>
      <SetupProgress current="identity" />

      <form className={styles.panel} onSubmit={create} noValidate aria-labelledby="identity-title">
        <h2 id="identity-title" className={styles.panelTitle}>{t('platform.setup.step.identity')}</h2>
        <p className={styles.panelHint}>{t('platform.identity.hint')}</p>
        {error && <ErrorBanner message={error} />}

        <div className={styles.formGrid}>
          <div className={styles.field}>
            <label htmlFor={ids.name} className={styles.label}>{t('platform.identity.name')}</label>
            <input id={ids.name} className={styles.input} value={form.name} maxLength={limits.nameMax} autoComplete="off"
              aria-invalid={!!problems.name} aria-describedby={`${ids.name}-msg`} onChange={(e) => setName(e.target.value)} />
            <span id={`${ids.name}-msg`} className={problems.name ? styles.fieldError : styles.hint}>
              {message('name') ?? t('platform.identity.nameHint')}
            </span>
          </div>

          <div className={styles.field}>
            <label htmlFor={ids.slug} className={styles.label}>{t('platform.identity.slug')}</label>
            <input id={ids.slug} className={`${styles.input} ${styles.mono}`} dir="ltr" value={form.slug} maxLength={limits.slugMax}
              autoComplete="off" spellCheck={false} aria-invalid={!!problems.slug} aria-describedby={`${ids.slug}-msg`}
              onChange={(e) => { setSlugTouched(true); set('slug')({ target: { value: e.target.value.toLowerCase() } }); }} />
            <span id={`${ids.slug}-msg`} className={problems.slug ? styles.fieldError : styles.hint}>
              {message('slug') ?? t('platform.identity.slugHint')}
            </span>
          </div>

          <div className={styles.field}>
            <label htmlFor={ids.currency} className={styles.label}>{t('platform.identity.currency')}</label>
            <input id={ids.currency} className={`${styles.input} ${styles.mono}`} dir="ltr" value={form.currency} maxLength={3}
              autoComplete="off" spellCheck={false} aria-invalid={!!problems.currency}
              aria-describedby={`${ids.currency}-msg`}
              onChange={(e) => set('currency')({ target: { value: e.target.value.toUpperCase() } })} />
            <span id={`${ids.currency}-msg`} className={problems.currency ? styles.fieldError : styles.hint}>
              {message('currency') ?? t('platform.identity.currencyHint')}
            </span>
          </div>

          <div className={styles.field}>
            <label htmlFor={ids.culture} className={styles.label}>{t('platform.identity.defaultCulture')}</label>
            <select id={ids.culture} className={styles.input} value={form.defaultCulture} onChange={set('defaultCulture')}>
              {options.data.settings.cultures.map((c) => <option key={c} value={c}>{t(`admin.settings.culture.${c}`)}</option>)}
            </select>
            <span className={styles.hint}>{t('platform.identity.cultureHint')}</span>
          </div>

          <div className={styles.field}>
            <label htmlFor={ids.zone} className={styles.label}>{t('admin.settings.timeZone')}</label>
            <input id={ids.zone} className={`${styles.input} ${styles.mono}`} dir="ltr" list="settings-time-zones" value={form.timeZone}
              maxLength={limits.timeZoneMax} aria-invalid={!!problems.timeZone} aria-describedby={`${ids.zone}-msg`}
              onChange={set('timeZone')} />
            <TimeZones />
            <span id={`${ids.zone}-msg`} className={problems.timeZone ? styles.fieldError : styles.hint}>
              {message('timeZone') ?? t('admin.settings.timeZoneHint')}
            </span>
          </div>
        </div>

        <p className={styles.notice} role="note">{t('platform.identity.createsClosed')}</p>
        <div className={styles.formActions}>
          <Link to="/platform/stores" className={styles.secondaryLink}>{t('common.cancel')}</Link>
          <Button type="submit" variant="primary" loading={busy}>{t('platform.identity.create')}</Button>
        </div>
      </form>
    </div>
  );
}
