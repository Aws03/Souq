import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import Drawer from '../../components/common/Drawer';
import FormField, { inputClass } from '../../components/common/FormField';
import Button from '../../components/common/Button';
import { ErrorBanner } from '../../components/common/StateViews';
import { buildCouponPayload, couponFormProblem, couponToForm } from '../../features/admin/coupons/couponForm';
import { getStoreCurrency } from '../../app/tenantModel';
import styles from './CategoryFormDrawer.module.css';

// درج إضافة/تعديل كوبون (المرحلة 10: نافذة بدء وانتهاء، وحدّ لكل عميل). الرمز لا يتغيّر بعد الإنشاء.
export default function CouponFormDrawer({ coupon, onSave, onClose }) {
  const { t } = useTranslation();
  const currency = getStoreCurrency();   // مبلغ الخصم الثابت بعملة المتجر (المرحلة 15، A5)
  const isEdit = !!coupon;
  const [form, setForm] = useState(() => couponToForm(coupon));
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);

  const set = (key) => (e) => setForm((f) => ({ ...f, [key]: e.target.type === 'checkbox' ? e.target.checked : e.target.value }));

  const submit = async (e) => {
    e.preventDefault();
    const problem = couponFormProblem(form, isEdit);
    if (problem) return setError(t(`admin.couponForm.${problem}`));

    setBusy(true); setError(null);
    try { await onSave(buildCouponPayload(form, isEdit)); }
    catch (err) { setError(err.message); setBusy(false); }
  };

  return (
    <Drawer open onClose={onClose} side="right" busy={busy} title={isEdit ? t('admin.couponForm.editTitle') : t('admin.couponForm.addTitle')}
      footer={
        <div className={styles.footActions}>
          <Button variant="ghost" onClick={onClose} disabled={busy}>{t('common.cancel')}</Button>
          <Button variant="primary" type="submit" form="coupon-form" loading={busy}>{t('common.save')}</Button>
        </div>
      }>
      <form id="coupon-form" onSubmit={submit}>
        {error && <ErrorBanner message={error} />}

        <FormField label={t('admin.couponForm.codeLabel')} hint={isEdit ? t('admin.couponForm.codeHint') : undefined}>
          <input className={inputClass(false)} value={form.code} disabled={isEdit} dir="ltr"
            onChange={(e) => setForm((f) => ({ ...f, code: e.target.value.toUpperCase() }))} placeholder="SAVE10" />
        </FormField>

        <FormField label={t('admin.couponForm.typeLabel')}>
          <select className={inputClass(false)} value={form.type} onChange={set('type')}>
            <option value="Percentage">{t('admin.couponForm.typePercentage')}</option>
            <option value="FixedAmount">{t('admin.couponForm.typeFixed', { currency })}</option>
          </select>
        </FormField>

        <FormField label={form.type === 'Percentage' ? t('admin.couponForm.percentageLabel') : t('admin.couponForm.amountLabel', { currency })}>
          <input className={inputClass(false)} type="number" min="0" step="0.01" value={form.value} onChange={set('value')} />
        </FormField>

        <FormField label={t('admin.couponForm.minOrderLabel')}>
          <input className={inputClass(false)} type="number" min="0" step="0.01" value={form.minOrderAmount} onChange={set('minOrderAmount')} />
        </FormField>

        <FormField label={t('admin.couponForm.startsLabel')}>
          <input className={inputClass(false)} type="date" value={form.startsAt} onChange={set('startsAt')} />
        </FormField>

        <FormField label={t('admin.couponForm.expiresLabel')}>
          <input className={inputClass(false)} type="date" value={form.expiresAt} onChange={set('expiresAt')} />
        </FormField>

        <FormField label={t('admin.couponForm.maxUsesLabel')}>
          <input className={inputClass(false)} type="number" min="1" step="1" value={form.maxUses} onChange={set('maxUses')} />
        </FormField>

        <FormField label={t('admin.couponForm.perCustomerLabel')} hint={t('admin.couponForm.perCustomerHint')}>
          <input className={inputClass(false)} type="number" min="1" step="1" value={form.maxUsesPerCustomer} onChange={set('maxUsesPerCustomer')} />
        </FormField>

        {isEdit && (
          <FormField label="">
            <label className={styles.checkboxRow}>
              <input type="checkbox" checked={form.isActive} onChange={set('isActive')} />
              {t('admin.couponForm.activeLabel')}
            </label>
          </FormField>
        )}
      </form>
    </Drawer>
  );
}
