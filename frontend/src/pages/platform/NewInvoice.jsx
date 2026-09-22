import { useId, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../../api/client';
import { queryKeys } from '../../app/queryKeys';
import Button from '../../components/common/Button';
import Skeleton from '../../components/common/Skeleton';
import { ErrorBanner } from '../../components/common/StateViews';
import { useDebouncedValue } from '../../hooks/useDebouncedValue';
import styles from './Platform.module.css';

// ============================================================================
// مسوّدةُ فاتورةٍ جديدة لمتجر (C5، ADR-0056).
//
// **وهذه الشاشة أضيفت لأنّ رحلةَ المتصفّح كشفت غيابَها**: كان كلُّ ما تحتاجه C5 موجوداً إلّا
// أوّلَ خطوةٍ فيه — فلا يستطيع مشغّلٌ أن يبدأ فاتورةً من لوحته، وكان لا بدّ من نداءِ API يدويّ.
// قدرةٌ أوّلُ خطوةٍ فيها خارج الواجهة ليست قدرةً يملكها مشغّل، وهذا بالضبط ما تقيسه الرحلة.
//
// **والمسوّدةُ لا تُصدِر شيئاً.** تُنشَأ، ثمّ تُراجَع أسطرُها في شاشتها، ثمّ تُصدَر بفعلٍ منفصل
// خلف تأكيد — فلا تخرج فاتورةٌ إلى تاجرٍ بضغطةٍ واحدة.
//
// **ولا مبلغَ يُكتب هنا حين يكون للخطة سعر**: «أضف سطر الاشتراك» يأخذه من سعر الخطة المجمَّد،
// والخادمُ يرفض إن لم تكن مُسعَّرة (`PlanNotPriced`) بدل أن يخترع رقماً.
// ============================================================================
export default function NewInvoice() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const ids = { store: useId(), from: useId(), to: useId() };

  const [search, setSearch] = useState('');
  const term = useDebouncedValue(search.trim());
  const [form, setForm] = useState(() => {
    // الشهرُ التقويميّ الحاليّ افتراضاً، بنهايةٍ حصريّة — كفترة الفوترة في الخادم تماماً،
    // فلا تتداخل فاتورتان ولا تسقط لحظةٌ بينهما.
    const now = new Date();
    const first = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), 1));
    const next = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth() + 1, 1));
    return {
      tenantId: '',
      periodStartUtc: first.toISOString().slice(0, 10),
      periodEndUtc: next.toISOString().slice(0, 10),
      includeSubscription: false,
    };
  });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);

  const params = { search: term || undefined, page: 1, pageSize: 50 };
  const stores = useQuery({
    queryKey: queryKeys.platformStores(params),
    queryFn: () => api.getPlatformStores(params),
  });

  const settings = useQuery({
    queryKey: queryKeys.platformBillingSettings(),
    queryFn: api.getPlatformBillingSettings,
  });

  const blocked = settings.data && !settings.data.canIssue;

  const create = async (event) => {
    event.preventDefault();
    setBusy(true); setError(null);
    try {
      const { id } = await api.createPlatformInvoice({
        tenantId: Number(form.tenantId),
        periodStartUtc: new Date(`${form.periodStartUtc}T00:00:00Z`).toISOString(),
        periodEndUtc: new Date(`${form.periodEndUtc}T00:00:00Z`).toISOString(),
        includeSubscription: form.includeSubscription,
        billingPeriodId: null,
        lines: [],
      });
      await queryClient.invalidateQueries({ queryKey: queryKeys.platformInvoicesAll() });
      navigate(`/platform/invoices/${id}`, { replace: true });
    } catch (err) {
      setError(err.message);
      setBusy(false);
    }
  };

  return (
    <div className={styles.setup}>
      <Link to="/platform/invoices" className={styles.back}>{t('platform.invoices.backToList')}</Link>
      <h1 className={styles.title}>{t('platform.invoices.newTitle')}</h1>
      <p className={styles.subtitle}>{t('platform.invoices.newSubtitle')}</p>

      {/* الإعدادُ ناقصٌ ⇒ لا مسوّدةَ أصلاً، والسببُ مكتوبٌ قبل أن يُملأ النموذج. */}
      {blocked && (
        <p className={styles.notice} role="status">
          {t(`platform.billing.blocked.${settings.data.blockingReason ?? 'BillingSettingsMissing'}`)}
          {' '}
          <Link to="/platform/billing" className={styles.rowLink}>{t('platform.billing.configure')}</Link>
        </p>
      )}

      <form className={styles.panel} onSubmit={create} noValidate aria-labelledby="new-invoice">
        <h2 id="new-invoice" className={styles.panelTitle}>{t('platform.invoices.newTitle')}</h2>
        {error && <ErrorBanner message={error} />}

        <div className={styles.formGrid}>
          <div className={styles.field}>
            <label htmlFor={`${ids.store}-search`} className={styles.label}>
              {t('platform.invoices.storeSearch')}
            </label>
            <input
              id={`${ids.store}-search`}
              className={styles.input}
              type="search"
              value={search}
              placeholder={t('platform.invoices.storeSearchPlaceholder')}
              onChange={(e) => setSearch(e.target.value)}
            />
          </div>

          <div className={styles.field}>
            <label htmlFor={ids.store} className={styles.label}>{t('platform.invoices.store')}</label>
            {stores.isPending
              ? <Skeleton height={42} radius={10} />
              : (
                <select
                  id={ids.store}
                  className={styles.input}
                  value={form.tenantId}
                  required
                  onChange={(e) => setForm((f) => ({ ...f, tenantId: e.target.value }))}
                >
                  <option value="">{t('platform.invoices.chooseStore')}</option>
                  {(stores.data?.items ?? []).map((store) => (
                    <option key={store.id} value={store.id}>{store.name} ({store.slug})</option>
                  ))}
                </select>
              )}
          </div>

          <div className={styles.field}>
            <label htmlFor={ids.from} className={styles.label}>{t('platform.invoices.periodFrom')}</label>
            <input id={ids.from} className={styles.input} type="date" required value={form.periodStartUtc}
              onChange={(e) => setForm((f) => ({ ...f, periodStartUtc: e.target.value }))} />
          </div>

          <div className={styles.field}>
            <label htmlFor={ids.to} className={styles.label}>{t('platform.invoices.periodTo')}</label>
            <input id={ids.to} className={styles.input} type="date" required value={form.periodEndUtc}
              onChange={(e) => setForm((f) => ({ ...f, periodEndUtc: e.target.value }))} />
            <span className={styles.hint}>{t('platform.invoices.periodHint')}</span>
          </div>
        </div>

        <label className={styles.option}>
          <input
            type="checkbox"
            checked={form.includeSubscription}
            onChange={(e) => setForm((f) => ({ ...f, includeSubscription: e.target.checked }))}
          />
          <span>
            {t('platform.invoices.includeSubscription')}
            <span className={styles.hint}> {t('platform.invoices.includeSubscriptionHint')}</span>
          </span>
        </label>

        <p className={styles.notice} role="note">{t('platform.invoices.createsDraft')}</p>

        <div className={styles.formActions}>
          <Link to="/platform/invoices" className={styles.secondaryLink}>{t('common.cancel')}</Link>
          <Button type="submit" variant="primary" loading={busy} disabled={!form.tenantId || blocked}>
            {t('platform.invoices.createDraft')}
          </Button>
        </div>
      </form>
    </div>
  );
}
