import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import Drawer from '../../components/common/Drawer';
import Button from '../../components/common/Button';
import FormField, { inputClass } from '../../components/common/FormField';
import { ErrorBanner } from '../../components/common/StateViews';
import { buildVariantPricingPayload, validateVariantPricing } from '../../features/admin/products/variantModel';
import formStyles from './CategoryFormDrawer.module.css';

// درج تسعير متغيّر واحد: السعر، سعر ما قبل الخصم، وSKU (بعملة المنتج على الخادم؛ SKU فريد في المتجر يرفضه الخادم برمزه).
export default function EditVariantDrawer({ variant, title, skuMaxLength, onSave, onClose }) {
  const { t } = useTranslation();
  const [price, setPrice] = useState(variant.price ?? '');
  const [compareAtPrice, setCompareAtPrice] = useState(variant.compareAtPrice ?? '');
  const [sku, setSku] = useState(variant.sku ?? '');
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);

  const submit = async (e) => {
    e.preventDefault();
    const problem = validateVariantPricing({ price, compareAtPrice });
    if (problem) return setError(t(`admin.variants.errors.${problem}`));

    setBusy(true); setError(null);
    try {
      await onSave(buildVariantPricingPayload({ price, compareAtPrice, sku }));
    } catch (err) {
      setError(err.message);
      setBusy(false);
    }
  };

  return (
    <Drawer open onClose={onClose} side="right" busy={busy} title={t('admin.variants.editTitle', { name: title })}
      footer={
        <div className={formStyles.footActions}>
          <Button variant="ghost" onClick={onClose} disabled={busy}>{t('common.cancel')}</Button>
          <Button variant="primary" type="submit" form="variant-form" loading={busy}>{t('common.save')}</Button>
        </div>
      }>
      <form id="variant-form" onSubmit={submit}>
        {error && <ErrorBanner message={error} />}
        <FormField label={t('admin.variants.priceLabel')}>
          <input className={inputClass(false)} type="number" min="0" step="0.001" dir="ltr" value={price}
            onChange={(e) => setPrice(e.target.value)} />
        </FormField>
        <FormField label={t('admin.variants.compareAtLabel')} hint={t('admin.variants.compareAtHint')}>
          <input className={inputClass(false)} type="number" min="0" step="0.001" dir="ltr" value={compareAtPrice}
            onChange={(e) => setCompareAtPrice(e.target.value)} />
        </FormField>
        <FormField label={t('admin.variants.skuLabel')} hint={t('admin.variants.skuHint')}>
          <input className={inputClass(false)} dir="ltr" maxLength={skuMaxLength} value={sku} placeholder="TEE-M-RED"
            onChange={(e) => setSku(e.target.value)} />
        </FormField>
      </form>
    </Drawer>
  );
}
