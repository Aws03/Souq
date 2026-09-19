import { useId, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { api } from '../../api/client';
import { queryKeys } from '../../app/queryKeys';
import { useAuth } from '../../context/AuthContext';
import DataTable from '../../components/common/DataTable';
import Pagination from '../../components/common/Pagination';
import Button from '../../components/common/Button';
import Drawer from '../../components/common/Drawer';
import {
  ACTION_GROUPS, ACTION_MAX, KNOWN_ACTIONS, actionKey, actionsInGroup, actorKind, auditQuery, emptyFilters,
  filterProblems, filtersFromSearch, hasFilters, isKnownAction, pageFromSearch, readMetadata, searchFromFilters,
} from '../../features/platform/audit';
import { dateLocale, dateOptions } from '../../app/dateLocale';
import styles from './Platform.module.css';

// ============================================================================
// سجلّ التدقيق (platform.audit.view — المالك والمشرف): من فعل ماذا، في أيّ متجر، ومتى.
//
// الشاشة تقول ما يقوله السطر فقط. الخادم يسجّل معرّف الفاعل ودوره لا اسمه، ومعرّف المتجر لا اسمه — فيُعرضان
// معرّفين، ولا تُجلب أسماء بطلبات إضافية (كل قراءة من المنصّة سطر تدقيق جديد، وحساب المتجر خارج نطاق المنصّة).
// وتقول ما لا يحويه السجلّ: الطلبات المرفوضة أو الفاشلة، والدخول، وأفعال المتسوّقين — غيابها هنا ليس دليلاً
// على أنها لم تحدث.
// ============================================================================
// الثواني تهمّ في تحقيق (أيّ الطلبين سبق). مضيف المنصّة بلا متجر يحدّد منطقته: توقيت من يقرأ، ويُقال ذلك.
// اللحظة من الخادم UTC بلاحقة Z (UtcDateTimeJsonConverter).
function formatAuditTime(value, language) {
  return new Date(value).toLocaleString(dateLocale(language, ''), dateOptions({ dateStyle: 'medium', timeStyle: 'medium' }));
}

export default function Audit() {
  const { t, i18n } = useTranslation();
  const { user, can } = useAuth();
  const [search, setSearch] = useSearchParams();
  const applied = filtersFromSearch(search);
  const page = pageFromSearch(search);
  const [selected, setSelected] = useState(null);

  const params = auditQuery(applied, page);
  const { data, error, isPending, isFetching, refetch } = useQuery({
    queryKey: queryKeys.platformAudit(params),
    queryFn: () => api.getPlatformAudit(params),
    placeholderData: keepPreviousData,
  });

  const apply = (filters, nextPage = 1) => setSearch(searchFromFilters(filters, nextPage));
  const canOpenStores = can('platform.tenants.manage');

  const storeCell = (entry) => {
    if (entry.tenantId == null) return <span className={styles.hint}>{t('platform.audit.noStore')}</span>;
    const label = t('platform.audit.storeRef', { id: entry.tenantId });
    return canOpenStores ? <Link to={`/platform/stores/${entry.tenantId}`} className={styles.rowLink}>{label}</Link> : label;
  };

  const columns = [
    {
      key: 'when', header: t('platform.audit.colWhen'), width: '170px',
      render: (e) => <time dateTime={e.occurredAt}>{formatAuditTime(e.occurredAt, i18n.language)}</time>,
    },
    {
      key: 'action', header: t('platform.audit.colAction'), width: '250px',
      render: (e) => <ActionLabel action={e.action} />,
    },
    { key: 'store', header: t('platform.audit.colStore'), width: '120px', render: storeCell },
    { key: 'actor', header: t('platform.audit.colActor'), width: '170px', render: (e) => <ActorLabel entry={e} userId={user?.id} /> },
    {
      key: 'target', header: t('platform.audit.colTarget'), width: '170px', truncate: true,
      tooltip: (e) => [e.targetType, e.targetId].filter(Boolean).join(' '),
      render: (e) => (e.targetType || e.targetId
        ? <span dir="ltr" className={`${styles.mono} ${styles.auditCode}`}>{[e.targetType, e.targetId].filter(Boolean).join(' · ')}</span>
        : '—'),
    },
    {
      key: 'details', header: t('platform.audit.colDetails'), width: '100px', align: 'end',
      render: (e) => (
        <Button size="sm" variant="ghost" onClick={() => setSelected(e)}
          aria-label={t('platform.audit.detailsFor', { id: e.id })}>
          {t('platform.audit.details')}
        </Button>
      ),
    },
  ];

  const filtered = hasFilters(applied);

  return (
    <div>
      <div className={styles.pageHead}>
        <div>
          <h1 className={styles.title}>{t('platform.audit.title')}</h1>
          <p className={styles.subtitle}>{t('platform.audit.subtitle')}</p>
        </div>
      </div>

      <details className={styles.auditScope}>
        <summary>{t('platform.audit.scope.summary')}</summary>
        <ul>
          <li>{t('platform.audit.scope.recorded')}</li>
          <li>{t('platform.audit.scope.notRecorded')}</li>
          <li>{t('platform.audit.scope.identity')}</li>
          <li>{t('platform.audit.scope.selfRecorded')}</li>
          <li>{t('platform.audit.scope.timeZone')}</li>
        </ul>
      </details>

      {/* key: الرجوع في المتصفّح يغيّر الرابط — النموذج يُبنى من جديد بما يقوله الرابط لا بما كُتب قبله. */}
      <AuditFilters key={search.toString()} applied={applied} onApply={apply} busy={isFetching} />

      {data && (
        <p className={styles.resultCount} role="status">
          {t(filtered ? 'platform.audit.countFiltered' : 'platform.audit.count', { count: data.totalCount })}
        </p>
      )}

      <DataTable label={t('platform.audit.title')} columns={columns} rows={data?.items ?? []} rowKey={(e) => e.id} loading={isPending}
        error={error?.message} onRetry={refetch}
        emptyTitle={filtered ? t('platform.audit.noMatchTitle') : t('platform.audit.emptyTitle')}
        emptyMessage={filtered ? t('platform.audit.noMatchMessage') : t('platform.audit.emptyMessage')}
        minWidth="1000px" />

      {data && <Pagination page={page} totalPages={data.totalPages} onChange={(next) => apply(applied, next)} />}

      <EntryDrawer entry={selected} userId={user?.id} canOpenStores={canOpenStores}
        onClose={() => setSelected(null)}
        onFilter={(patch) => { setSelected(null); apply({ ...emptyFilters(), ...patch }); }} />
    </div>
  );
}

function ActionLabel({ action }) {
  const { t } = useTranslation();
  return (
    <span className={styles.auditAction}>
      {isKnownAction(action) && <span>{t(`platform.audit.action.${actionKey(action)}`)}</span>}
      <code dir="ltr" className={styles.mono}>{action}</code>
    </span>
  );
}

const KNOWN_ROLES = ['PlatformOwner', 'PlatformAdmin', 'TenantAdmin', 'TenantStaff'];
const roleLabel = (t, role) => (KNOWN_ROLES.includes(role) ? t(`platform.audit.role.${role}`) : role);

function ActorLabel({ entry, userId }) {
  const { t } = useTranslation();
  const kind = actorKind(entry, userId);
  if (kind === 'system') return t('platform.audit.actor.system');
  if (kind === 'unknown') return <span className={styles.hint}>{t('platform.audit.actor.unknown')}</span>;
  return (
    <span className={styles.auditAction}>
      <span>{kind === 'you' ? t('platform.audit.actor.you', { id: entry.actorUserId }) : t('platform.audit.actor.account', { id: entry.actorUserId })}</span>
      {entry.actorRole && <span className={styles.hint}>{roleLabel(t, entry.actorRole)}</span>}
    </span>
  );
}

function AuditFilters({ applied, onApply, busy }) {
  const { t } = useTranslation();
  const ids = { store: useId(), action: useId(), actor: useId(), from: useId(), to: useId() };
  const [form, setForm] = useState(applied);
  const [submitted, setSubmitted] = useState(false);
  const problems = submitted ? filterProblems(form) : {};
  const set = (field) => (e) => setForm((f) => ({ ...f, [field]: e.target.value }));

  const submit = (event) => {
    event.preventDefault();
    setSubmitted(true);
    if (Object.keys(filterProblems(form)).length > 0) return;
    onApply(form);
  };

  const clear = () => { setSubmitted(false); onApply(emptyFilters()); };
  const custom = form.action && !ACTION_GROUPS.some((g) => g.prefix === form.action) && !KNOWN_ACTIONS.includes(form.action);
  const message = (field) => (problems[field] ? t(`platform.audit.problem.${problems[field]}`) : null);

  return (
    <form className={`${styles.panel} ${styles.auditFilters}`} onSubmit={submit} noValidate
      aria-label={t('platform.audit.filtersLabel')}>
      <div className={styles.field}>
        <label htmlFor={ids.store} className={styles.label}>{t('platform.audit.filter.store')}</label>
        <input id={ids.store} className={styles.input} inputMode="numeric" dir="ltr" autoComplete="off" value={form.tenantId}
          aria-invalid={!!problems.tenantId} aria-describedby={`${ids.store}-msg`} onChange={set('tenantId')} />
        <span id={`${ids.store}-msg`} className={problems.tenantId ? styles.fieldError : styles.hint}>
          {message('tenantId') ?? t('platform.audit.filter.storeHint')}
        </span>
      </div>

      <div className={styles.field}>
        <label htmlFor={ids.action} className={styles.label}>{t('platform.audit.filter.action')}</label>
        <select id={ids.action} className={styles.input} value={form.action} onChange={set('action')}
          aria-describedby={`${ids.action}-msg`}>
          <option value="">{t('platform.audit.filter.allActions')}</option>
          {custom && <option value={form.action}>{form.action.slice(0, ACTION_MAX)}</option>}
          <optgroup label={t('platform.audit.filter.groups')}>
            {ACTION_GROUPS.map((g) => <option key={g.key} value={g.prefix}>{t(`platform.audit.group.${g.key}`)}</option>)}
          </optgroup>
          {ACTION_GROUPS.map((g) => (
            <optgroup key={g.key} label={t(`platform.audit.group.${g.key}`)}>
              {actionsInGroup(g.key).map((a) => <option key={a} value={a}>{t(`platform.audit.action.${actionKey(a)}`)}</option>)}
            </optgroup>
          ))}
        </select>
        <span id={`${ids.action}-msg`} className={problems.action ? styles.fieldError : styles.hint}>
          {message('action') ?? t('platform.audit.filter.actionHint')}
        </span>
      </div>

      <div className={styles.field}>
        <label htmlFor={ids.actor} className={styles.label}>{t('platform.audit.filter.actor')}</label>
        <input id={ids.actor} className={styles.input} inputMode="numeric" dir="ltr" autoComplete="off" value={form.actorUserId}
          aria-invalid={!!problems.actorUserId} aria-describedby={`${ids.actor}-msg`} onChange={set('actorUserId')} />
        <span id={`${ids.actor}-msg`} className={problems.actorUserId ? styles.fieldError : styles.hint}>
          {message('actorUserId') ?? t('platform.audit.filter.actorHint')}
        </span>
      </div>

      <div className={styles.field}>
        <label htmlFor={ids.from} className={styles.label}>{t('platform.audit.filter.from')}</label>
        <input id={ids.from} type="date" className={styles.input} value={form.from} max={form.to || undefined}
          onChange={set('from')} />
      </div>

      <div className={styles.field}>
        <label htmlFor={ids.to} className={styles.label}>{t('platform.audit.filter.to')}</label>
        <input id={ids.to} type="date" className={styles.input} value={form.to} min={form.from || undefined}
          aria-invalid={!!problems.to} aria-describedby={`${ids.to}-msg`} onChange={set('to')} />
        <span id={`${ids.to}-msg`} className={problems.to ? styles.fieldError : styles.hint}>
          {message('to') ?? t('platform.audit.filter.toHint')}
        </span>
      </div>

      <div className={styles.formActions}>
        {hasFilters(applied) && <Button variant="ghost" onClick={clear}>{t('platform.audit.filter.clear')}</Button>}
        <Button type="submit" variant="primary" disabled={busy}>{t('platform.audit.filter.apply')}</Button>
      </div>
    </form>
  );
}

function EntryDrawer({ entry, userId, canOpenStores, onClose, onFilter }) {
  const { t, i18n } = useTranslation();
  if (!entry) return null;
  const metadata = readMetadata(entry.metadata);
  const kind = actorKind(entry, userId);

  return (
    <Drawer open onClose={onClose} side="right" width={480} title={t('platform.audit.entryTitle', { id: entry.id })}>
      <div className={styles.auditEntry}>
        <ActionLabel action={entry.action} />

        <dl className={styles.auditFacts}>
          <div>
            <dt>{t('platform.audit.colWhen')}</dt>
            <dd>
              {formatAuditTime(entry.occurredAt, i18n.language)}
              <span dir="ltr" className={`${styles.hint} ${styles.mono}`}>{entry.occurredAt}</span>
            </dd>
          </div>
          <div><dt>{t('platform.audit.area')}</dt><dd>{t(`platform.audit.areaName.${entry.area}`, { defaultValue: entry.area })}</dd></div>
          <div>
            <dt>{t('platform.audit.colStore')}</dt>
            <dd>
              {entry.tenantId == null ? t('platform.audit.noStore') : canOpenStores
                ? <Link to={`/platform/stores/${entry.tenantId}`} className={styles.rowLink}>{t('platform.audit.storeRef', { id: entry.tenantId })}</Link>
                : t('platform.audit.storeRef', { id: entry.tenantId })}
            </dd>
          </div>
          <div><dt>{t('platform.audit.colActor')}</dt><dd><ActorLabel entry={entry} userId={userId} /></dd></div>
          <div>
            <dt>{t('platform.audit.colTarget')}</dt>
            <dd dir="ltr" className={styles.mono}>{[entry.targetType, entry.targetId].filter(Boolean).join(' · ') || '—'}</dd>
          </div>
          <div><dt>{t('platform.audit.ipAddress')}</dt><dd dir="ltr" className={styles.mono}>{entry.ipAddress ?? '—'}</dd></div>
          <div>
            <dt>{t('platform.audit.correlationId')}</dt>
            <dd dir="ltr" className={styles.mono}>{entry.correlationId ?? '—'}</dd>
          </div>
        </dl>

        <section aria-labelledby="audit-metadata-title">
          <h3 id="audit-metadata-title" className={styles.auditSubtitle}>{t('platform.audit.metadata')}</h3>
          {metadata.entries.length > 0 && (
            <dl className={styles.auditFacts}>
              {metadata.entries.map(([key, value]) => (
                <div key={key}>
                  <dt dir="ltr" className={styles.mono}>{key}</dt>
                  <dd dir="ltr" className={styles.mono}>{value ?? '—'}</dd>
                </div>
              ))}
            </dl>
          )}
          {metadata.raw && <pre dir="ltr" className={styles.auditRaw}>{metadata.raw}</pre>}
          {metadata.entries.length === 0 && !metadata.raw && <p className={styles.empty}>{t('platform.audit.noMetadata')}</p>}
        </section>

        <div className={styles.lifecycleActions}>
          {entry.tenantId != null && (
            <Button size="sm" variant="ghost" onClick={() => onFilter({ tenantId: String(entry.tenantId) })}>
              {t('platform.audit.showStore')}
            </Button>
          )}
          {(kind === 'account' || kind === 'you') && (
            <Button size="sm" variant="ghost" onClick={() => onFilter({ actorUserId: String(entry.actorUserId) })}>
              {t('platform.audit.showActor')}
            </Button>
          )}
        </div>
      </div>
    </Drawer>
  );
}
