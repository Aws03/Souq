import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api } from '../../api/client';
import { useToast } from '../../context/ToastContext';
import Button from '../../components/common/Button';
import FormField, { inputClass } from '../../components/common/FormField';
import { ErrorBanner } from '../../components/common/StateViews';
import {
  buildVariantsPayload, combinationKey, missingCombinations, remainingVariantCapacity, validateVariantPricing,
  variantLabel,
} from '../../features/admin/products/variantModel';
import styles from './ProductVariants.module.css';

// ============================================================================
// "أنشئ التركيبات الناقصة" (ADR-0040): التركيبات التي لا متغيّر لها بعد (المعطّل يُعدّ موجوداً)، يختار المدير منها ما يبيعه،
// بسعر ومخزون افتتاحي مشتركين يُعدَّلان لكل متغيّر بعدها. الخادم يتحقّق من كل تركيبة داخل المنتج ويفتح مخزون كلٍّ منها.
// التفعيل افتراضياً فقط إن كان المنتج مخفياً من الواجهة أصلاً: متغيّر نشط ثانٍ يُخفي منتجاً يُباع الآن (بوّابة V2).
// ============================================================================
export default function CreateVariantsPanel({ product, lang, onCreated }) {
  const { t } = useTranslation();
  const toast = useToast();
  const missing = missingCombinations(product.options, product.variants);
  const capacity = remainingVariantCapacity(product);
  const [selected, setSelected] = useState(() => new Set());
  const [price, setPrice] = useState(product.price ?? '');
  const [compareAtPrice, setCompareAtPrice] = useState('');
  const [initialStock, setInitialStock] = useState('0');
  const [lowStockThreshold, setLowStockThreshold] = useState('');
  // المتغيّر الجديد نشط افتراضياً: منذ V3 يُعرض المنتج ويُشترى باختيار صريح، فلا سبب لإنشائه معطّلاً.
  const [isActive, setIsActive] = useState(true);
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);

  const toggle = (key) => setSelected((current) => {
    const next = new Set(current);
    if (next.has(key)) next.delete(key); else next.add(key);
    return next;
  });

  const create = async () => {
    const chosen = missing.filter((ids) => selected.has(combinationKey(ids)));
    if (chosen.length === 0) return setError(t('admin.variants.errors.noneSelected'));
    if (chosen.length > capacity) return setError(t('admin.variants.errors.overCapacity', { count: capacity }));
    const pricing = validateVariantPricing({ price, compareAtPrice });
    if (pricing) return setError(t(`admin.variants.errors.${pricing}`));

    setBusy(true); setError(null);
    try {
      await api.createProductVariants(product.id,
        buildVariantsPayload(chosen, { price, compareAtPrice, initialStock, lowStockThreshold, isActive }));
      toast.success(t('admin.variants.created', { count: chosen.length }));
      setSelected(new Set());
      onCreated();
    } catch (err) {
      setError(err.message);
    } finally {
      setBusy(false);
    }
  };

  if (product.options.length === 0) {
    return (
      <section className={styles.card} aria-labelledby="create-title">
        <h3 id="create-title" className={styles.cardTitle}>{t('admin.variants.createTitle')}</h3>
        <p className={styles.empty}>{t('admin.variants.needOptions')}</p>
      </section>
    );
  }

  return (
    <section className={styles.card} aria-labelledby="create-title">
      <header className={styles.cardHead}>
        <div>
          <h3 id="create-title" className={styles.cardTitle}>{t('admin.variants.createTitle')}</h3>
          <p className={styles.cardHint}>{t('admin.variants.createHint')}</p>
        </div>
      </header>

      {error && <ErrorBanner message={error} />}

      {capacity === 0 && <p className={styles.empty}>{t('admin.variants.capacityReached', { max: product.variantLimits.maxVariants })}</p>}
      {capacity > 0 && missing.length === 0 && <p className={styles.empty}>{t('admin.variants.noMissing')}</p>}

      {capacity > 0 && missing.length > 0 && (
        <>
          <div className={styles.selectionBar}>
            <span className={styles.cardHint}>{t('admin.variants.capacityHint', { count: capacity })}</span>
            <div className={styles.footActions}>
              <Button variant="ghost" size="sm" disabled={busy}
                onClick={() => setSelected(new Set(missing.slice(0, capacity).map(combinationKey)))}>
                {t('admin.variants.selectAll')}
              </Button>
              <Button variant="ghost" size="sm" disabled={busy || selected.size === 0} onClick={() => setSelected(new Set())}>
                {t('admin.variants.clearSelection')}
              </Button>
            </div>
          </div>

          <fieldset className={styles.fieldset}>
            <legend className="souq-visually-hidden">{t('admin.variants.combinationsLabel')}</legend>
            <ul className={styles.combinations}>
              {missing.map((ids) => {
                const key = combinationKey(ids);
                return (
                  <li key={key}>
                    <label className={`${styles.combination} ${selected.has(key) ? styles.combinationOn : ''}`}>
                      <input type="checkbox" checked={selected.has(key)} onChange={() => toggle(key)} disabled={busy} />
                      <span dir="auto">{variantLabel(product.options, ids, lang)}</span>
                    </label>
                  </li>
                );
              })}
            </ul>
          </fieldset>

          <div className={styles.sharedGrid}>
            <FormField label={t('admin.variants.priceLabel')}>
              <input className={inputClass(false)} type="number" min="0" step="0.001" dir="ltr" value={price}
                onChange={(e) => setPrice(e.target.value)} />
            </FormField>
            <FormField label={t('admin.variants.compareAtLabel')} hint={t('admin.variants.compareAtHint')}>
              <input className={inputClass(false)} type="number" min="0" step="0.001" dir="ltr" value={compareAtPrice}
                onChange={(e) => setCompareAtPrice(e.target.value)} />
            </FormField>
            <FormField label={t('admin.variants.initialStock')}>
              <input className={inputClass(false)} type="number" min="0" step="1" dir="ltr" value={initialStock}
                onChange={(e) => setInitialStock(e.target.value)} />
            </FormField>
            <FormField label={t('admin.variants.lowStock')}>
              <input className={inputClass(false)} type="number" min="0" step="1" dir="ltr" value={lowStockThreshold}
                placeholder="5" onChange={(e) => setLowStockThreshold(e.target.value)} />
            </FormField>
          </div>

          <label className={styles.toggle}>
            <input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} disabled={busy} />
            <span>
              {t('admin.variants.makeActive')}
              <small className={styles.cardHint}>{t('admin.variants.makeActiveHint')}</small>
            </span>
          </label>

          <footer className={styles.cardFoot}>
            <span />
            <Button variant="primary" loading={busy} disabled={selected.size === 0} onClick={create}>
              {t('admin.variants.create', { count: selected.size })}
            </Button>
          </footer>
        </>
      )}
    </section>
  );
}
