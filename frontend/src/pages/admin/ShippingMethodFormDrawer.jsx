import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import Drawer from '../../components/common/Drawer';
import FormField, { inputClass } from '../../components/common/FormField';
import Button from '../../components/common/Button';
import { ErrorBanner } from '../../components/common/StateViews';
import { buildMethodPayload, methodFormProblem, methodToForm } from '../../features/admin/shipping/shippingForm';
import styles from './CategoryFormDrawer.module.css';

// درج إضافة/تعديل طريقة شحن (المرحلة 12): السعر بعملة المتجر، مجانية فوق حدّ اختياري، مدّة تقديرية، ناقل ورابط تتبّع
// بعلامة {number}، ودول تخدمها (فارغة ⇒ كل مكان).
export default function ShippingMethodFormDrawer({ method, onSave, onClose }) {
  const { t } = useTranslation();
  const isEdit = !!method;
  const [form, setForm] = useState(() => methodToForm(method));
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);

  const set = (key) => (e) => setForm((f) => ({ ...f, [key]: e.target.type === 'checkbox' ? e.target.checked : e.target.value }));

  const submit = async (e) => {
    e.preventDefault();
    const problem = methodFormProblem(form);
    if (problem) return setError(t(`admin.shipping.form.${problem}`));

    setBusy(true); setError(null);
    try { await onSave(buildMethodPayload(form)); }
    catch (err) { setError(err.message); setBusy(false); }
  };

  return (
    <Drawer open onClose={onClose} side="right" busy={busy}
      title={isEdit ? t('admin.shipping.form.editTitle') : t('admin.shipping.form.addTitle')}
      footer={
        <div className={styles.footActions}>
          <Button variant="ghost" onClick={onClose} disabled={busy}>{t('common.cancel')}</Button>
          <Button variant="primary" type="submit" form="shipping-method-form" loading={busy}>{t('common.save')}</Button>
        </div>
      }>
      <form id="shipping-method-form" onSubmit={submit}>
        {error && <ErrorBanner message={error} />}

        <FormField label={t('admin.shipping.form.nameLabel')}>
          <input className={inputClass(false)} value={form.name} onChange={set('name')} maxLength={100} />
        </FormField>

        <FormField label={t('admin.shipping.form.priceLabel')}>
          <input className={inputClass(false)} type="number" min="0" step="any" value={form.price} onChange={set('price')} />
        </FormField>

        <FormField label={t('admin.shipping.form.freeOverLabel')}>
          <input className={inputClass(false)} type="number" min="0" step="any" value={form.freeOverAmount} onChange={set('freeOverAmount')} />
        </FormField>

        <FormField label={t('admin.shipping.form.minDaysLabel')}>
          <input className={inputClass(false)} type="number" min="0" max="90" step="1" value={form.minDays} onChange={set('minDays')} />
        </FormField>

        <FormField label={t('admin.shipping.form.maxDaysLabel')}>
          <input className={inputClass(false)} type="number" min="0" max="90" step="1" value={form.maxDays} onChange={set('maxDays')} />
        </FormField>

        <FormField label={t('admin.shipping.form.carrierLabel')}>
          <input className={inputClass(false)} value={form.carrier} onChange={set('carrier')} maxLength={100} />
        </FormField>

        <FormField label={t('admin.shipping.form.trackingUrlLabel')} hint={t('admin.shipping.form.trackingUrlHint')}>
          <input className={inputClass(false)} dir="ltr" value={form.trackingUrlTemplate} onChange={set('trackingUrlTemplate')}
            placeholder="https://carrier.example/track?n={number}" />
        </FormField>

        <FormField label={t('admin.shipping.form.countriesLabel')} hint={t('admin.shipping.form.countriesHint')}>
          <input className={inputClass(false)} dir="ltr" value={form.countries} onChange={set('countries')} placeholder="JO, SA" />
        </FormField>

        <FormField label={t('admin.shipping.form.sortOrderLabel')}>
          <input className={inputClass(false)} type="number" min="0" step="1" value={form.sortOrder} onChange={set('sortOrder')} />
        </FormField>

        <FormField label="">
          <label className={styles.checkboxRow}>
            <input type="checkbox" checked={form.isActive} onChange={set('isActive')} />
            {t('admin.shipping.form.activeLabel')}
          </label>
        </FormField>
      </form>
    </Drawer>
  );
}
