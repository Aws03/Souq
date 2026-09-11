import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api } from '../../api/client';
import { useToast } from '../../context/ToastContext';
import FormField, { inputClass } from '../../components/common/FormField';
import Button from '../../components/common/Button';
import Spinner from '../../components/common/Spinner';
import { ErrorBanner } from '../../components/common/StateViews';
import { formatDateTime } from '../../i18n';
import { accountFormProblem, accountToForm, buildAccountPayload, publishableMode } from '../../features/admin/payments/paymentView';
import adminStyles from './Admin.module.css';
import styles from './Payments.module.css';

// حساب بوّابة الدفع الخاص بالمتجر (المرحلة 11، store.payments.manage): بدونه يقبض المتجر في حساب المنصّة الافتراضي. السرّان
// حقلا كتابة فقط — لا يعودان من الخادم أبداً، وتركهما فارغين يبقي المحفوظ. الخادم يرفض المفاتيح التجريبية حيث لا تُسمح.
export default function Payments() {
  const { t } = useTranslation();
  const toast = useToast();
  const [account, setAccount] = useState(null);
  const [form, setForm] = useState(accountToForm(null));
  const [error, setError] = useState(null);
  const [loadError, setLoadError] = useState(null);
  const [busy, setBusy] = useState(false);

  const load = useCallback(() => {
    api.getStorePayments()
      .then((a) => { setAccount(a); setForm(accountToForm(a)); setLoadError(null); })
      .catch((e) => setLoadError(e.message));
  }, []);

  useEffect(() => { load(); }, [load]);

  const set = (key) => (e) => setForm((f) => ({ ...f, [key]: e.target.value }));

  const save = async (e) => {
    e.preventDefault();
    const problem = accountFormProblem(form, account.usesStoreAccount)
      ?? (!account.testKeysAllowed && publishableMode(form.publishableKey) === 'test' ? 'testKeysNotAllowed' : null);
    if (problem) return setError(t(`admin.payments.form.${problem}`));

    setBusy(true); setError(null);
    try {
      await api.updateStorePayments(buildAccountPayload(form));
      toast.success(t('admin.payments.saved'));
      load();
    } catch (err) {
      setError(err.message);
    } finally {
      setBusy(false);
    }
  };

  const disconnect = async () => {
    if (!window.confirm(t('admin.payments.confirmDisconnect'))) return;
    setBusy(true);
    try {
      await api.removeStorePayments();
      toast.success(t('admin.payments.disconnected'));
      load();
    } catch (err) {
      toast.error(err.message);
    } finally {
      setBusy(false);
    }
  };

  if (loadError) return <ErrorBanner message={loadError} />;
  if (!account) return <div className={styles.loading}><Spinner size={26} /></div>;

  const mode = account.usesStoreAccount ? (account.liveMode ? 'live' : 'test') : null;

  return (
    <div>
      <h2 className={adminStyles.pageTitle}>{t('admin.payments.title')}</h2>
      <p className={adminStyles.pageSub}>{t('admin.payments.subtitle')}</p>

      <section className={styles.card}>
        <div className={styles.statusRow}>
          <b>{account.usesStoreAccount ? t('admin.payments.storeAccount') : t('admin.payments.platformAccount')}</b>
          {mode && (
            <span className={`${adminStyles.statusBadge} ${mode === 'live' ? adminStyles.delivered : adminStyles.pending}`}>
              {t(`admin.payments.mode.${mode}`)}
            </span>
          )}
        </div>
        <p className={styles.hint}>
          {account.usesStoreAccount
            ? t('admin.payments.storeAccountHint', { hint: account.secretKeyHint, date: formatDateTime(account.updatedAt) })
            : t('admin.payments.platformAccountHint')}
        </p>
      </section>

      {!account.canStoreSecrets ? (
        <ErrorBanner message={t('admin.payments.secretsNotConfigured')} />
      ) : (
        <form className={styles.card} onSubmit={save}>
          {error && <ErrorBanner message={error} />}

          <FormField label={t('admin.payments.form.publishableLabel')} hint={t('admin.payments.form.publishableHint')}>
            <input className={inputClass(false)} dir="ltr" autoComplete="off" placeholder="pk_live_…"
              value={form.publishableKey} onChange={set('publishableKey')} />
          </FormField>

          <FormField label={t('admin.payments.form.secretLabel')}
            hint={account.usesStoreAccount
              ? t('admin.payments.form.keepSecretHint', { hint: account.secretKeyHint })
              : t('admin.payments.form.secretHint')}>
            <input className={inputClass(false)} dir="ltr" type="password" autoComplete="new-password" placeholder="sk_live_…"
              value={form.secretKey} onChange={set('secretKey')} />
          </FormField>

          <FormField label={t('admin.payments.form.webhookLabel')}
            hint={account.hasWebhookSecret ? t('admin.payments.form.keepWebhookHint') : t('admin.payments.form.webhookHint')}>
            <input className={inputClass(false)} dir="ltr" type="password" autoComplete="new-password" placeholder="whsec_…"
              value={form.webhookSecret} onChange={set('webhookSecret')} />
          </FormField>

          <div className={styles.actions}>
            {account.usesStoreAccount && (
              <Button variant="ghost" type="button" onClick={disconnect} disabled={busy}>{t('admin.payments.disconnect')}</Button>
            )}
            <Button variant="primary" type="submit" loading={busy}>{t('common.save')}</Button>
          </div>
        </form>
      )}
    </div>
  );
}
