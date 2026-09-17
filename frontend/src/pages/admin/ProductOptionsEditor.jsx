import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api } from '../../api/client';
import { useToast } from '../../context/ToastContext';
import Button from '../../components/common/Button';
import FormField, { inputClass } from '../../components/common/FormField';
import { ErrorBanner } from '../../components/common/StateViews';
import { ChevronIcon, PlusIcon, TrashIcon } from '../../components/icons/Icons';
import {
  buildOptionsPayload, nameIn, newOption, newValue, optionsToForm, usedValueIds, validateOptionsForm,
} from '../../features/admin/products/variantModel';
import styles from './ProductVariants.module.css';

// ============================================================================
// نموذج خيارات المنتج (ADR-0040): الخيارات بقيمها بالعربية والإنجليزية، إضافةً وحذفاً وترتيباً، ثم حفظ واحد يستبدل التعريف
// كاملاً (PUT options). قيمة يستخدمها متغيّر لا يُعرض لها زرّ حذف فعّال — مرآة لقاعدة الخادم الذي يرفضها برمزه على أي حال.
// خيار جديد لمنتج له متغيّرات يسأل صراحةً عن قيمة المتغيّرات الحالية. الحدود من الخادم (variantLimits).
// ============================================================================
export default function ProductOptionsEditor({ product, defaultCulture, lang, onSaved }) {
  const { t } = useTranslation();
  const toast = useToast();
  const limits = product.variantLimits;
  const [initial] = useState(() => optionsToForm(product.options));
  const [form, setForm] = useState(initial);
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);

  const used = usedValueIds(product.variants);
  const dirty = JSON.stringify(buildOptionsPayload(form)) !== JSON.stringify(buildOptionsPayload(initial));

  const update = (optionKey, change) =>
    setForm((current) => current.map((option) => (option.key === optionKey ? change(option) : option)));

  const setOptionName = (optionKey, culture) => (e) =>
    update(optionKey, (option) => ({ ...option, names: { ...option.names, [culture]: e.target.value } }));

  const setValueName = (optionKey, valueKey, culture) => (e) => update(optionKey, (option) => ({
    ...option,
    values: option.values.map((value) => (value.key === valueKey ? { ...value, names: { ...value.names, [culture]: e.target.value } } : value)),
  }));

  const move = (list, index, delta) => {
    const target = index + delta;
    if (target < 0 || target >= list.length) return list;
    const next = [...list];
    [next[index], next[target]] = [next[target], next[index]];
    return next;
  };

  const save = async () => {
    const problem = validateOptionsForm(form, limits, defaultCulture);
    if (problem) return setError(t(`admin.variants.errors.${problem.key}`, problem.values));

    setBusy(true); setError(null);
    try {
      await api.setProductOptions(product.id, buildOptionsPayload(form));
      toast.success(t('admin.variants.optionsSaved'));
      onSaved();
    } catch (err) {
      setError(err.message);
      setBusy(false);
    }
  };

  const valueLabel = (value, index) => nameIn(value.names, lang) || `#${index + 1}`;

  return (
    <section className={styles.card} aria-labelledby="options-title">
      <header className={styles.cardHead}>
        <div>
          <h3 id="options-title" className={styles.cardTitle}>{t('admin.variants.optionsTitle')}</h3>
          <p className={styles.cardHint}>
            {t('admin.variants.optionsHint', { maxOptions: limits.maxOptions, maxValues: limits.maxValuesPerOption })}
          </p>
        </div>
        {dirty && <span className={styles.unsaved} role="status">{t('admin.variants.unsaved')}</span>}
      </header>

      {error && <ErrorBanner message={error} />}
      {form.length === 0 && <p className={styles.empty}>{t('admin.variants.noOptions')}</p>}

      <ol className={styles.optionList}>
        {form.map((option, optionIndex) => (
          <li key={option.key} className={styles.option}>
            <fieldset className={styles.fieldset}>
              <legend className={styles.legend}>
                {t('admin.variants.optionHeading', { position: optionIndex + 1 })}
                {nameIn(option.names, lang) && <span className={styles.legendName} dir="auto"> · {nameIn(option.names, lang)}</span>}
              </legend>

              <div className={styles.optionTools}>
                <button type="button" className={styles.iconBtn} onClick={() => setForm((c) => move(c, optionIndex, -1))}
                  disabled={busy || optionIndex === 0} aria-label={t('admin.variants.moveUp')} title={t('admin.variants.moveUp')}>
                  <ChevronIcon dir="up" />
                </button>
                <button type="button" className={styles.iconBtn} onClick={() => setForm((c) => move(c, optionIndex, 1))}
                  disabled={busy || optionIndex === form.length - 1} aria-label={t('admin.variants.moveDown')} title={t('admin.variants.moveDown')}>
                  <ChevronIcon dir="down" />
                </button>
                <button type="button" className={`${styles.textBtn} ${styles.danger}`} disabled={busy}
                  onClick={() => setForm((c) => c.filter((o) => o.key !== option.key))}>
                  <TrashIcon size={14} /> {t('admin.variants.removeOption')}
                </button>
              </div>

              <div className={styles.pair}>
                <FormField label={t('admin.variants.optionNameAr')}>
                  <input className={inputClass(false)} dir="rtl" maxLength={limits.nameMaxLength}
                    value={option.names.ar ?? ''} onChange={setOptionName(option.key, 'ar')} placeholder="المقاس" />
                </FormField>
                <FormField label={t('admin.variants.optionNameEn')}>
                  <input className={inputClass(false)} dir="ltr" maxLength={limits.nameMaxLength}
                    value={option.names.en ?? ''} onChange={setOptionName(option.key, 'en')} placeholder="Size" />
                </FormField>
              </div>

              <div className={styles.valuesHead}>{t('admin.variants.valuesLabel')}</div>
              <ul className={styles.valueList}>
                {option.values.map((value, valueIndex) => {
                  const inUse = value.id !== null && used.has(value.id);
                  return (
                    <li key={value.key} className={styles.valueRow}>
                      <input className={inputClass(false)} dir="rtl" maxLength={limits.nameMaxLength}
                        aria-label={`${t('admin.variants.valueNameAr')} ${valueIndex + 1}`}
                        value={value.names.ar ?? ''} onChange={setValueName(option.key, value.key, 'ar')} />
                      <input className={inputClass(false)} dir="ltr" maxLength={limits.nameMaxLength}
                        aria-label={`${t('admin.variants.valueNameEn')} ${valueIndex + 1}`}
                        value={value.names.en ?? ''} onChange={setValueName(option.key, value.key, 'en')} />
                      <div className={styles.valueTools}>
                        <button type="button" className={styles.iconBtn} disabled={busy || valueIndex === 0}
                          onClick={() => update(option.key, (o) => ({ ...o, values: move(o.values, valueIndex, -1) }))}
                          aria-label={t('admin.variants.moveUp')} title={t('admin.variants.moveUp')}>
                          <ChevronIcon dir="up" size={14} />
                        </button>
                        <button type="button" className={styles.iconBtn} disabled={busy || valueIndex === option.values.length - 1}
                          onClick={() => update(option.key, (o) => ({ ...o, values: move(o.values, valueIndex, 1) }))}
                          aria-label={t('admin.variants.moveDown')} title={t('admin.variants.moveDown')}>
                          <ChevronIcon dir="down" size={14} />
                        </button>
                        <button type="button" className={`${styles.iconBtn} ${styles.danger}`} disabled={busy || inUse}
                          onClick={() => update(option.key, (o) => ({ ...o, values: o.values.filter((v) => v.key !== value.key) }))}
                          aria-label={t('admin.variants.removeValue', { name: valueLabel(value, valueIndex) })}
                          title={inUse ? t('admin.variants.valueInUse') : t('admin.variants.removeValue', { name: valueLabel(value, valueIndex) })}>
                          <TrashIcon size={14} />
                        </button>
                      </div>
                      {inUse && <small className={styles.valueNote}>{t('admin.variants.valueInUse')}</small>}
                    </li>
                  );
                })}
              </ul>
              <Button variant="ghost" size="sm" disabled={busy || option.values.length >= limits.maxValuesPerOption}
                onClick={() => update(option.key, (o) => ({ ...o, values: [...o.values, newValue()] }))}>
                <PlusIcon size={14} /> {t('admin.variants.addValue')}
              </Button>

              {option.id === null && product.variants.length > 0 && (
                <FormField label={t('admin.variants.existingVariantsValue')} hint={t('admin.variants.existingVariantsHint')}>
                  <select className={inputClass(false)} value={option.existingVariantsValue ?? ''}
                    onChange={(e) => update(option.key, (o) => ({ ...o, existingVariantsValue: e.target.value }))}>
                    {option.values.map((value, valueIndex) => (
                      <option key={value.key} value={value.key}>{valueLabel(value, valueIndex)}</option>
                    ))}
                  </select>
                </FormField>
              )}
            </fieldset>
          </li>
        ))}
      </ol>

      <footer className={styles.cardFoot}>
        <Button variant="ghost" disabled={busy || form.length >= limits.maxOptions} onClick={() => setForm((c) => [...c, newOption()])}>
          <PlusIcon size={14} /> {t('admin.variants.addOption')}
        </Button>
        <div className={styles.footActions}>
          <Button variant="ghost" disabled={busy || !dirty} onClick={() => { setForm(initial); setError(null); }}>
            {t('admin.variants.resetOptions')}
          </Button>
          <Button variant="primary" loading={busy} disabled={!dirty} onClick={save}>{t('admin.variants.saveOptions')}</Button>
        </div>
      </footer>
    </section>
  );
}
