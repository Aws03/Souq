import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import Drawer from '../../components/common/Drawer';
import FormField, { inputClass } from '../../components/common/FormField';
import Button from '../../components/common/Button';
import { ErrorBanner } from '../../components/common/StateViews';
import { addressToForm, emptyAddress, formToAddress, missingAddressFields } from '../../features/account/addressForm';
import styles from './Account.module.css';

const FIELDS = [
  { key: 'label', placeholder: 'account.field.labelPlaceholder' },
  { key: 'recipientName', autoComplete: 'name' },
  { key: 'phone', type: 'tel', dir: 'ltr', autoComplete: 'tel' },
  { key: 'country', dir: 'ltr', maxLength: 2, autoComplete: 'country' },
  { key: 'city', autoComplete: 'address-level2' },
  { key: 'region', autoComplete: 'address-level1' },
  { key: 'line1', autoComplete: 'address-line1' },
  { key: 'line2', autoComplete: 'address-line2' },
  { key: 'postalCode', dir: 'ltr', autoComplete: 'postal-code' },
];

// درج إضافة/تعديل عنوان في دفتر العميل. الحقول الإلزامية تُفحص هنا للتجربة فقط؛ صيغة الهاتف ورمز الدولة والأطوال
// يحرسها الكيان (PostalAddress) ويعيد 422 InvalidCustomerData. اختيار الافتراضي عند الإضافة فقط — بعدها من البطاقة.
export default function AddressFormDrawer({ address, onSave, onClose }) {
  const { t } = useTranslation();
  const isEdit = !!address?.id;
  const [form, setForm] = useState(() => (isEdit ? addressToForm(address) : emptyAddress()));
  const [defaults, setDefaults] = useState({ shipping: false, billing: false });
  const [submitted, setSubmitted] = useState(false);
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);

  const missing = missingAddressFields(form);
  const setField = (key) => (e) => setForm((f) => ({ ...f, [key]: e.target.value }));
  const setDefault = (key) => (e) => setDefaults((d) => ({ ...d, [key]: e.target.checked }));

  const submit = async (e) => {
    e.preventDefault();
    setSubmitted(true);
    if (missing.length > 0) return;
    setBusy(true); setError(null);
    try { await onSave(formToAddress(form), defaults); }
    catch (err) { setError(err.message); setBusy(false); }
  };

  return (
    <Drawer open onClose={onClose} side="right" busy={busy}
      title={t(isEdit ? 'account.addressFormEdit' : 'account.addressFormAdd')}
      footer={
        <div className={styles.drawerActions}>
          <Button variant="ghost" onClick={onClose} disabled={busy}>{t('common.cancel')}</Button>
          <Button variant="primary" type="submit" form="address-form" loading={busy}>{t('common.save')}</Button>
        </div>
      }>
      <form id="address-form" onSubmit={submit} noValidate>
        {error && <ErrorBanner message={error} />}
        {FIELDS.map(({ key, placeholder, ...input }) => {
          const invalid = submitted && missing.includes(key);
          return (
            <FormField key={key} label={t(`account.field.${key}`)} htmlFor={`address-${key}`}
              error={invalid && t('account.fieldRequired')}>
              <input id={`address-${key}`} className={inputClass(invalid)} value={form[key]} onChange={setField(key)}
                placeholder={placeholder ? t(placeholder) : undefined} {...input} />
            </FormField>
          );
        })}
        {!isEdit && (
          <div className={styles.defaultChecks}>
            <label className={styles.checkboxRow}>
              <input type="checkbox" checked={defaults.shipping} onChange={setDefault('shipping')} />
              {t('account.useAsDefaultShipping')}
            </label>
            <label className={styles.checkboxRow}>
              <input type="checkbox" checked={defaults.billing} onChange={setDefault('billing')} />
              {t('account.useAsDefaultBilling')}
            </label>
          </div>
        )}
      </form>
    </Drawer>
  );
}
