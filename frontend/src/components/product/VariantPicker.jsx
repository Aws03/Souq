import { useTranslation } from 'react-i18next';
import { nameIn } from '../../features/catalog/variantLabel';
import { sortedOptions } from '../../features/catalog/variantSelection';
import styles from './VariantPicker.module.css';

// ============================================================================
// اختيار المتغيّر في صفحة المنتج (V3، P-08c): مجموعة أزرار اختيار حقيقية لكل خيار.
//
//   • <fieldset> + <legend> لكل خيار، و<input type="radio"> لكل قيمة — فالمجموعة واسمها وحالتها تُقرأ وتُتنقّل
//     بلوحة المفاتيح بلا أدوار ARIA مصنوعة يدوياً.
//   • القيمة التي لا يمكن شراؤها الآن تُعرض معطّلة لا مخفيّة (P-08c)، وسببها يُقرأ ("نفد" أو "غير متاح مع اختيارك")
//     بنصّ مخفيّ بصرياً داخل تسميتها — لا لوناً وحده.
//   • القيمة المختارة تبقى قابلة للتحديد حتى إن نفدت: هي حالة المتسوّق الحالية، ومنعُها يُخفي ما اختاره.
//   • لا شيء يُختار تلقائياً هنا: الاختيار صريح، والمنطق كله في variantSelection.js.
// ============================================================================
export default function VariantPicker({ product, selection, states, onSelect, lang }) {
  const { t } = useTranslation();

  return (
    <div className={styles.picker}>
      {sortedOptions(product).map((option) => {
        const selectedId = selection?.[option.id];
        const selectedValue = option.values.find((value) => value.id === selectedId);
        return (
          <fieldset key={option.id} className={styles.option}>
            <legend className={styles.legend}>
              <span className={styles.optionName}>{nameIn(option.names, lang)}</span>
              {selectedValue
                ? <span className={styles.chosen} dir="auto">{nameIn(selectedValue.names, lang)}</span>
                : <span className={styles.chooseHint}>{t('product.variant.choose')}</span>}
            </legend>
            <div className={styles.values}>
              {option.values.map((value) => {
                const state = states[value.id] ?? { purchasable: true, selected: false, reason: null };
                const disabled = !state.purchasable && !state.selected;
                return (
                  <label key={value.id} className={`${styles.value} ${state.selected ? styles.selected : ''} ${disabled ? styles.disabled : ''}`}>
                    <input type="radio" className="souq-visually-hidden" name={`product-option-${option.id}`}
                      value={value.id} checked={!!state.selected} disabled={disabled}
                      onChange={() => onSelect(option.id, value.id)} />
                    <span className={styles.valueName} dir="auto">{nameIn(value.names, lang)}</span>
                    {state.reason && (
                      <span className="souq-visually-hidden">
                        {' — '}{t(state.reason === 'soldOut' ? 'product.variant.soldOut' : 'product.variant.unavailableWithSelection')}
                      </span>
                    )}
                  </label>
                );
              })}
            </div>
          </fieldset>
        );
      })}
    </div>
  );
}
