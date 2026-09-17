import { useId, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api } from '../../api/client';
import { useToast } from '../../context/ToastContext';
import Button from '../../components/common/Button';
import ConfirmDialog from '../../components/common/ConfirmDialog';
import { ErrorBanner } from '../../components/common/StateViews';
import { formatDate } from '../../i18n';
import {
  activationWarnings, adminInviteProblems, canRemoveDomain, hostProblem, lifecycleActions, normalizeHost,
  readiness,
} from '../../features/platform/provisioning';
import styles from './Platform.module.css';

// ============================================================================
// أقسام متجر واحد من منظور المنصّة — مشتركة بين معالج التجهيز وصفحة المتجر، فخطوة "النطاقات" في المعالج هي
// قسم النطاقات في الصفحة نفسه لا نسخة منه. كل قسم يحفظ فوراً عبر نقطته، ثم يستدعي onChanged لإعادة القراءة:
// ما يُعرض بعد الإجراء هو ما خزّنه الخادم، لا ما افترضته الواجهة.
// ============================================================================

export function StatusBadge({ status }) {
  const { t } = useTranslation();
  return <span className={`${styles.badge} ${styles[`status${status}`] ?? ''}`}>{t(`platform.status.${status}`)}</span>;
}

// ── الجاهزية ────────────────────────────────────────────────────────────────
const STATE_MARK = { done: '✓', attention: '!', missing: '○', blocked: '–' };

export function ReadinessPanel({ store, accounts, headingLevel = 2 }) {
  const { t } = useTranslation();
  const ready = readiness(store, accounts);
  const Heading = `h${headingLevel}`;
  return (
    <section className={styles.panel} aria-labelledby="readiness-title">
      <Heading id="readiness-title" className={styles.panelTitle}>{t('platform.readiness.title')}</Heading>
      <p className={styles.panelHint}>{t('platform.readiness.hint')}</p>
      <ul className={styles.checklist}>
        {ready.items.map((item) => (
          <li key={item.id} className={styles[`check_${item.state}`]}>
            <span className={styles.checkMark} aria-hidden="true">{STATE_MARK[item.state]}</span>
            <span>
              <b>{t(`platform.readiness.${item.id}.label`)}</b>
              <span className={styles.checkDetail}>
                {t(`platform.readiness.${item.id}.${item.state}`, {
                  ...item.values, status: item.values?.status ? t(`platform.status.${item.values.status}`) : undefined,
                })}
              </span>
            </span>
            <span className={styles.srOnly}>{t(`platform.readiness.state.${item.state}`)}</span>
          </li>
        ))}
      </ul>
    </section>
  );
}

// ── الملف: الاسم والعملة ─────────────────────────────────────────────────────
export function ProfilePanel({ store, options, onChanged }) {
  const { t } = useTranslation();
  const toast = useToast();
  const [form, setForm] = useState({ name: store.name, currency: store.currency });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);
  const nameId = useId();
  const currencyId = useId();

  const name = form.name.trim();
  const currency = form.currency.trim().toUpperCase();
  const nameInvalid = name.length < options.limits.nameMin || name.length > options.limits.nameMax;
  const currencyInvalid = !/^[A-Z]{3}$/.test(currency);
  const dirty = name !== store.name || currency !== store.currency;

  const save = async (event) => {
    event.preventDefault();
    if (nameInvalid || currencyInvalid || !dirty) return;
    setBusy(true); setError(null);
    try {
      await api.updatePlatformStore(store.id, { name, currency });
      toast.success(t('platform.profile.saved'));
      await onChanged();
    } catch (err) {
      setError(err.message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <section className={styles.panel} aria-labelledby="profile-title">
      <h2 id="profile-title" className={styles.panelTitle}>{t('platform.profile.title')}</h2>
      <form className={styles.formGrid} onSubmit={save} noValidate>
        {error && <ErrorBanner message={error} />}
        <div className={styles.field}>
          <label htmlFor={nameId} className={styles.label}>{t('platform.identity.name')}</label>
          <input id={nameId} className={styles.input} value={form.name} aria-invalid={nameInvalid}
            onChange={(e) => setForm((f) => ({ ...f, name: e.target.value }))} />
          {nameInvalid && <span className={styles.fieldError}>
            {t('platform.problem.nameLength', { min: options.limits.nameMin, max: options.limits.nameMax })}</span>}
        </div>
        <div className={styles.field}>
          <label htmlFor={currencyId} className={styles.label}>{t('platform.identity.currency')}</label>
          <input id={currencyId} className={`${styles.input} ${styles.mono}`} dir="ltr" maxLength={3} value={form.currency}
            aria-invalid={currencyInvalid} aria-describedby={`${currencyId}-hint`}
            onChange={(e) => setForm((f) => ({ ...f, currency: e.target.value.toUpperCase() }))} />
          <span id={`${currencyId}-hint`} className={currencyInvalid ? styles.fieldError : styles.hint}>
            {currencyInvalid ? t('platform.problem.currencyInvalid') : t('platform.profile.currencyHint')}
          </span>
        </div>
        <div className={styles.formActions}>
          <Button type="submit" variant="primary" loading={busy} disabled={!dirty || nameInvalid || currencyInvalid}>
            {t('common.save')}
          </Button>
        </div>
      </form>
    </section>
  );
}

// ── النطاقات ─────────────────────────────────────────────────────────────────
export function DomainsPanel({ store, options, onChanged }) {
  const { t } = useTranslation();
  const toast = useToast();
  const hostId = useId();
  const [host, setHost] = useState('');
  const [problem, setProblem] = useState(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);
  const [pending, setPending] = useState(null);   // { action, host }
  const [dialogError, setDialogError] = useState(null);

  const add = async (event) => {
    event.preventDefault();
    const found = hostProblem(host, options, store.domains);
    setProblem(found);
    if (found) return;
    setBusy(true); setError(null);
    try {
      await api.addPlatformStoreDomain(store.id, normalizeHost(host));
      toast.success(t('platform.domains.added', { host: normalizeHost(host) }));
      setHost('');
      await onChanged();
    } catch (err) {
      setError(err.message);
    } finally {
      setBusy(false);
    }
  };

  const run = async (action, target) => {
    const call = {
      primary: api.setPlatformStorePrimaryDomain, verify: api.verifyPlatformStoreDomain, remove: api.removePlatformStoreDomain,
    }[action];
    setBusy(true); setDialogError(null); setError(null);
    try {
      await call(store.id, target);
      toast.success(t(`platform.domains.done.${action}`, { host: target }));
      setPending(null);
      await onChanged();
    } catch (err) {
      if (pending) setDialogError(err.message); else setError(err.message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <section className={styles.panel} aria-labelledby="domains-title">
      <h2 id="domains-title" className={styles.panelTitle}>{t('platform.domains.title')}</h2>
      <p className={styles.panelHint}>{t('platform.domains.hint')}</p>
      {error && <ErrorBanner message={error} />}

      {store.domains.length === 0 ? (
        <p className={styles.empty}>{t('platform.domains.none')}</p>
      ) : (
        <ul className={styles.rows}>
          {store.domains.map((domain) => (
            <li key={domain.host} className={styles.row}>
              <span className={styles.rowMain}>
                <b dir="ltr" className={styles.host}>{domain.host}</b>
                <span className={styles.rowTags}>
                  {domain.isPrimary && <span className={`${styles.badge} ${styles.statusActive}`}>{t('platform.domains.primary')}</span>}
                  <span className={`${styles.badge} ${domain.verifiedAt ? styles.statusActive : styles.statusProvisioning}`}>
                    {domain.verifiedAt
                      ? t('platform.domains.verifiedOn', { date: formatDate(domain.verifiedAt) })
                      : t('platform.domains.unverified')}
                  </span>
                </span>
              </span>
              <span className={styles.rowActions}>
                {!domain.isPrimary && (
                  <Button size="sm" variant="ghost" disabled={busy} onClick={() => run('primary', domain.host)}>
                    {t('platform.domains.makePrimary')}
                  </Button>
                )}
                {!domain.verifiedAt && (
                  <Button size="sm" variant="ghost" disabled={busy} onClick={() => setPending({ action: 'verify', host: domain.host })}>
                    {t('platform.domains.verify')}
                  </Button>
                )}
                <Button size="sm" variant="danger" disabled={busy || !canRemoveDomain(domain, store.domains)}
                  title={canRemoveDomain(domain, store.domains) ? undefined : t('platform.domains.primaryLocked')}
                  onClick={() => setPending({ action: 'remove', host: domain.host })}>
                  {t('platform.domains.remove')}
                </Button>
              </span>
            </li>
          ))}
        </ul>
      )}

      <form className={styles.inlineForm} onSubmit={add} noValidate>
        <div className={styles.field}>
          <label htmlFor={hostId} className={styles.label}>{t('platform.domains.addLabel')}</label>
          <input id={hostId} className={`${styles.input} ${styles.mono}`} dir="ltr" autoComplete="off" spellCheck={false}
            placeholder="shop.example.com" value={host} aria-invalid={!!problem} aria-describedby={`${hostId}-hint`}
            onChange={(e) => { setHost(e.target.value); setProblem(null); }} />
          <span id={`${hostId}-hint`} className={problem ? styles.fieldError : styles.hint}>
            {problem ? t(`platform.problem.${problem.key}`) : t('platform.domains.addHint')}
          </span>
        </div>
        <Button type="submit" variant="primary" loading={busy && !pending}>{t('platform.domains.add')}</Button>
      </form>

      <ConfirmDialog open={!!pending} busy={busy} error={dialogError}
        danger={pending?.action === 'remove'}
        title={pending ? t(`platform.domains.confirm.${pending.action}.title`, { host: pending.host }) : ''}
        message={pending ? t(`platform.domains.confirm.${pending.action}.message`, { host: pending.host }) : ''}
        confirmLabel={pending ? t(`platform.domains.confirm.${pending.action}.action`) : ''}
        onConfirm={() => run(pending.action, pending.host)}
        onCancel={() => { setPending(null); setDialogError(null); }} />
    </section>
  );
}

// ── الوحدات ──────────────────────────────────────────────────────────────────
export function ModulesPanel({ store, options, onChanged }) {
  const { t } = useTranslation();
  const toast = useToast();
  const [selected, setSelected] = useState(() => new Set(store.modules));
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);
  const dirty = selected.size !== store.modules.length || store.modules.some((m) => !selected.has(m));

  const toggle = (module) => setSelected((current) => {
    const next = new Set(current);
    if (next.has(module)) next.delete(module); else next.add(module);
    return next;
  });

  const save = async (event) => {
    event.preventDefault();
    setBusy(true); setError(null);
    try {
      await api.setPlatformStoreModules(store.id, options.modules.filter((m) => selected.has(m)));
      toast.success(t('platform.modules.saved'));
      await onChanged();
    } catch (err) {
      setError(err.message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <section className={styles.panel} aria-labelledby="modules-title">
      <h2 id="modules-title" className={styles.panelTitle}>{t('platform.modules.title')}</h2>
      <p className={styles.panelHint}>{t('platform.modules.hint')}</p>
      <form onSubmit={save}>
        {error && <ErrorBanner message={error} />}
        <fieldset className={styles.fieldset}>
          <legend className={styles.srOnly}>{t('platform.modules.title')}</legend>
          {options.modules.map((module) => (
            <label key={module} className={styles.option}>
              <input type="checkbox" checked={selected.has(module)} onChange={() => toggle(module)} />
              <span>
                <b>{t(`platform.modules.name.${module}`)}</b>
                <span className={styles.hint}>{t(`platform.modules.description.${module}`)}</span>
              </span>
            </label>
          ))}
        </fieldset>
        <div className={styles.formActions}>
          <span className={styles.hint} aria-live="polite">{dirty ? t('platform.modules.unsaved') : t('platform.modules.upToDate')}</span>
          <Button type="submit" variant="primary" loading={busy} disabled={!dirty}>{t('common.save')}</Button>
        </div>
      </form>
    </section>
  );
}

// ── المديرون ─────────────────────────────────────────────────────────────────
export function AdminsPanel({ store, accounts, options, onChanged }) {
  const { t } = useTranslation();
  const toast = useToast();
  const nameId = useId();
  const emailId = useId();
  const [form, setForm] = useState({ fullName: '', email: '' });
  const [submitted, setSubmitted] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);

  const admins = accounts.filter((a) => a.role === 'TenantAdmin');
  const primary = store.domains.find((d) => d.isPrimary);
  const problems = submitted ? adminInviteProblems(form, options) : {};
  const archived = store.status === 'Archived';

  const invite = async (event) => {
    event.preventDefault();
    setSubmitted(true);
    if (Object.keys(adminInviteProblems(form, options)).length > 0) return;
    setBusy(true); setError(null);
    try {
      const email = form.email.trim().toLowerCase();
      const result = await api.invitePlatformStoreAdmin(store.id, { fullName: form.fullName.trim(), email });
      toast.success(t(result?.renewed ? 'platform.admins.renewed' : 'platform.admins.invited', { email, host: primary?.host }));
      setForm({ fullName: '', email: '' });
      setSubmitted(false);
      await onChanged();
    } catch (err) {
      setError(err.message);
    } finally {
      setBusy(false);
    }
  };

  const state = (a) => (a.status === 'Disabled' ? 'disabled' : a.invitationPending ? 'invited' : 'active');

  return (
    <section className={styles.panel} aria-labelledby="admins-title">
      <h2 id="admins-title" className={styles.panelTitle}>{t('platform.admins.title')}</h2>
      <p className={styles.panelHint}>{t('platform.admins.hint')}</p>

      {admins.length === 0 ? (
        <p className={styles.empty}>{t('platform.admins.none')}</p>
      ) : (
        <ul className={styles.rows}>
          {admins.map((a) => (
            <li key={a.id} className={styles.row}>
              <span className={styles.rowMain}>
                <b>{a.fullName}</b>
                <span dir="ltr" className={styles.hint}>{a.email}</span>
              </span>
              <span className={`${styles.badge} ${styles[`account_${state(a)}`]}`}>{t(`admin.staff.state.${state(a)}`)}</span>
            </li>
          ))}
        </ul>
      )}

      {!primary ? (
        <p className={styles.notice} role="note">{t('platform.admins.needsDomain')}</p>
      ) : archived ? (
        <p className={styles.notice} role="note">{t('platform.admins.archived')}</p>
      ) : (
        <form className={styles.formGrid} onSubmit={invite} noValidate>
          {error && <ErrorBanner message={error} />}
          <div className={styles.field}>
            <label htmlFor={nameId} className={styles.label}>{t('admin.staff.nameLabel')}</label>
            <input id={nameId} className={styles.input} value={form.fullName} autoComplete="off" aria-invalid={!!problems.fullName}
              onChange={(e) => setForm((f) => ({ ...f, fullName: e.target.value }))} />
            {problems.fullName && <span className={styles.fieldError}>{t(`platform.problem.${problems.fullName.key}`, problems.fullName.values)}</span>}
          </div>
          <div className={styles.field}>
            <label htmlFor={emailId} className={styles.label}>{t('admin.staff.emailLabel')}</label>
            <input id={emailId} type="email" dir="ltr" className={styles.input} value={form.email} autoComplete="off"
              aria-invalid={!!problems.email} onChange={(e) => setForm((f) => ({ ...f, email: e.target.value }))} />
            {problems.email && <span className={styles.fieldError}>{t(`platform.problem.${problems.email.key}`)}</span>}
          </div>
          <p className={styles.hint}>{t('platform.admins.linkHost', { host: primary.host })}</p>
          <div className={styles.formActions}>
            <Button type="submit" variant="primary" loading={busy}>{t('platform.admins.invite')}</Button>
          </div>
        </form>
      )}
    </section>
  );
}

// ── دورة الحياة ──────────────────────────────────────────────────────────────
export function LifecyclePanel({ store, accounts, onChanged }) {
  const { t } = useTranslation();
  const toast = useToast();
  const [pending, setPending] = useState(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);
  const actions = lifecycleActions(store.status);
  const warnings = activationWarnings(readiness(store, accounts));

  const confirm = async () => {
    setBusy(true); setError(null);
    try {
      await api.changePlatformStoreStatus(store.id, pending);
      toast.success(t(`platform.lifecycle.done.${pending}`, { name: store.name }));
      setPending(null);
      await onChanged();
    } catch (err) {
      setError(err.message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <section className={styles.panel} aria-labelledby="lifecycle-title">
      <h2 id="lifecycle-title" className={styles.panelTitle}>{t('platform.lifecycle.title')}</h2>
      <p className={styles.panelHint}>{t(`platform.lifecycle.current.${store.status}`)}</p>
      {actions.length > 0 && (
        <div className={styles.lifecycleActions}>
          {actions.map((action) => (
            <Button key={action} variant={action === 'Activate' ? 'primary' : 'danger'} onClick={() => setPending(action)}>
              {t(`platform.lifecycle.action.${action}`)}
            </Button>
          ))}
        </div>
      )}

      <ConfirmDialog open={!!pending} busy={busy} error={error} danger={pending !== 'Activate'}
        title={pending ? t(`platform.lifecycle.confirm.${pending}.title`, { name: store.name }) : ''}
        message={pending ? t(`platform.lifecycle.confirm.${pending}.message`, { name: store.name }) : ''}
        confirmLabel={pending ? t(`platform.lifecycle.action.${pending}`) : ''}
        requireText={pending === 'Archive' ? store.slug : null}
        onConfirm={confirm} onCancel={() => { setPending(null); setError(null); }}>
        {pending === 'Activate' && warnings.length > 0 && (
          <ul className={styles.warnings}>
            {warnings.map((w) => <li key={w}>{t(`platform.lifecycle.warning.${w}`)}</li>)}
          </ul>
        )}
      </ConfirmDialog>
    </section>
  );
}

