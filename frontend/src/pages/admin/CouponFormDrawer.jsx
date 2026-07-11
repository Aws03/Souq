import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import Drawer from '../../components/common/Drawer';
import FormField, { inputClass } from '../../components/common/FormField';
import Button from '../../components/common/Button';
import { ErrorBanner } from '../../components/common/StateViews';
import styles from './CategoryFormDrawer.module.css';

// تاريخ HTML (yyyy-MM-dd) ⇄ ISO — تحويل بسيط ذهاباً وإياباً لحقل <input type="date">.
const toDateInput = (iso) => (iso ? iso.slice(0, 10) : '');

export default function CouponFormDrawer({ coupon, onSave, onClose }) {
  const { t } = useTranslation();
  const isEdit = !!coupon;
  const [code, setCode] = useState(coupon?.code ?? '');
  const [type, setType] = useState(coupon?.type ?? 'Percentage');
  const [value, setValue] = useState(coupon?.value ?? '');
  const [minOrderAmount, setMinOrderAmount] = useState(coupon?.minOrderAmount ?? '');
  const [expiresAt, setExpiresAt] = useState(toDateInput(coupon?.expiresAt));
  const [maxUses, setMaxUses] = useState(coupon?.maxUses ?? '');
  const [isActive, setIsActive] = useState(coupon?.isActive ?? true);
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);

  const submit = async (e) => {
    e.preventDefault();
    if (!isEdit && !code.trim()) return setError(t('admin.couponForm.codeRequired'));
    if (!value || Number(value) <= 0) return setError(t('admin.couponForm.valueInvalid'));

    setBusy(true); setError(null);
    try {
      await onSave({
        ...(isEdit ? {} : { code: code.trim().toUpperCase() }),
        type, value: Number(value),
        minOrderAmount: minOrderAmount ? Number(minOrderAmount) : null,
        expiresAt: expiresAt ? new Date(expiresAt).toISOString() : null,
        maxUses: maxUses ? Number(maxUses) : null,
        isActive,
      });
    } catch (err) { setError(err.message); setBusy(false); }
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
          <input className={inputClass(false)} value={code} disabled={isEdit} dir="ltr"
            onChange={(e) => setCode(e.target.value.toUpperCase())} placeholder="SAVE10" />
        </FormField>

        <FormField label={t('admin.couponForm.typeLabel')}>
          <select className={inputClass(false)} value={type} onChange={(e) => setType(e.target.value)}>
            <option value="Percentage">{t('admin.couponForm.typePercentage')}</option>
            <option value="FixedAmount">{t('admin.couponForm.typeFixed')}</option>
          </select>
        </FormField>

        <FormField label={type === 'Percentage' ? t('admin.couponForm.percentageLabel') : t('admin.couponForm.amountLabel')}>
          <input className={inputClass(false)} type="number" min="0" step="0.01" value={value}
            onChange={(e) => setValue(e.target.value)} />
        </FormField>

        <FormField label={t('admin.couponForm.minOrderLabel')}>
          <input className={inputClass(false)} type="number" min="0" step="0.01" value={minOrderAmount}
            onChange={(e) => setMinOrderAmount(e.target.value)} />
        </FormField>

        <FormField label={t('admin.couponForm.expiresLabel')}>
          <input className={inputClass(false)} type="date" value={expiresAt} onChange={(e) => setExpiresAt(e.target.value)} />
        </FormField>

        <FormField label={t('admin.couponForm.maxUsesLabel')}>
          <input className={inputClass(false)} type="number" min="1" step="1" value={maxUses}
            onChange={(e) => setMaxUses(e.target.value)} />
        </FormField>

        {isEdit && (
          <FormField label="">
            <label className={styles.checkboxRow}>
              <input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} />
              {t('admin.couponForm.activeLabel')}
            </label>
          </FormField>
        )}
      </form>
    </Drawer>
  );
}
