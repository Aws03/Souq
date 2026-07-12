import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { CloseIcon } from '../icons/Icons';
import { formatPrice } from '../product/ProductBadges';
import styles from './FilterBar.module.css';

const SORT_KEYS = ['newest', 'priceAsc', 'priceDesc', 'bestSelling'];

// شريط فلاتر الكتالوج: فئات متعدّدة الاختيار (شرائح) + نطاق سعر + ترتيب،
// وتحته صف وسوم للفلاتر المفعّلة قابلة للإزالة فردياً وزر "مسح الكل".
// الحالة الفعلية في رابط الصفحة (يديرها Store) — هذا المكوّن عرضٌ وأحداث فقط.
export default function FilterBar({
  categories, filters, onToggleCategory, onPriceChange, onSortChange, onClearFilters,
}) {
  const { t } = useTranslation();
  const { categoryIds, minPrice, maxPrice, sortBy } = filters;

  // مسودة السعر محلية كي لا يُطلق طلب API مع كل رقم يُكتب — تُعتمد عند
  // Enter أو مغادرة الحقل، وتُزامَن حين يتغيّر الرابط خارجياً (إزالة وسم مثلاً).
  const [priceDraft, setPriceDraft] = useState({ min: minPrice, max: maxPrice });
  useEffect(() => { setPriceDraft({ min: minPrice, max: maxPrice }); }, [minPrice, maxPrice]);

  const commitPrice = () => {
    const clean = (v) => (v === '' || Number(v) < 0 || Number.isNaN(Number(v)) ? '' : v);
    onPriceChange(clean(priceDraft.min), clean(priceDraft.max));
  };
  const onPriceKeyDown = (e) => { if (e.key === 'Enter') { e.preventDefault(); commitPrice(); } };

  const activeCats = categories.filter((c) => categoryIds.includes(c.id));
  const hasPrice = minPrice !== '' || maxPrice !== '';
  const hasFilters = activeCats.length > 0 || hasPrice || sortBy !== 'newest';

  const priceTagText = () => {
    if (minPrice !== '' && maxPrice !== '') return `${formatPrice(minPrice)} – ${formatPrice(maxPrice)}`;
    if (minPrice !== '') return `≥ ${formatPrice(minPrice)}`;
    return `≤ ${formatPrice(maxPrice)}`;
  };

  return (
    <section className={styles.bar} aria-label={t('store.filters.aria')}>
      <div className={styles.inner}>
        <div className={styles.row}>
          <div className={styles.chips} role="group" aria-label={t('store.filters.categories')}>
            <button type="button"
              className={`${styles.chip} ${categoryIds.length === 0 ? styles.chipActive : ''}`}
              onClick={() => onToggleCategory(null)}>
              {t('nav.categoriesAll')}
            </button>
            {categories.map((c) => (
              <button key={c.id} type="button" aria-pressed={categoryIds.includes(c.id)}
                className={`${styles.chip} ${categoryIds.includes(c.id) ? styles.chipActive : ''}`}
                onClick={() => onToggleCategory(c.id)}>
                {c.name}
              </button>
            ))}
          </div>

          <div className={styles.controls}>
            <div className={styles.price}>
              <span className={styles.label}>{t('store.filters.price')}</span>
              <input type="number" min="0" inputMode="decimal" placeholder={t('store.filters.min')}
                aria-label={t('store.filters.minAria')} value={priceDraft.min}
                onChange={(e) => setPriceDraft((d) => ({ ...d, min: e.target.value }))}
                onBlur={commitPrice} onKeyDown={onPriceKeyDown} />
              <span className={styles.dash} aria-hidden="true">–</span>
              <input type="number" min="0" inputMode="decimal" placeholder={t('store.filters.max')}
                aria-label={t('store.filters.maxAria')} value={priceDraft.max}
                onChange={(e) => setPriceDraft((d) => ({ ...d, max: e.target.value }))}
                onBlur={commitPrice} onKeyDown={onPriceKeyDown} />
            </div>

            <label className={styles.sort}>
              <span className={styles.label}>{t('store.filters.sort')}</span>
              <select value={sortBy} onChange={(e) => onSortChange(e.target.value)}>
                {SORT_KEYS.map((k) => (
                  <option key={k} value={k}>{t(`store.filters.sort_${k}`)}</option>
                ))}
              </select>
            </label>
          </div>
        </div>

        {hasFilters && (
          <div className={styles.tags}>
            {activeCats.map((c) => (
              <button key={c.id} type="button" className={styles.tag}
                onClick={() => onToggleCategory(c.id)}
                aria-label={t('store.filters.remove', { name: c.name })}>
                {c.name} <CloseIcon size={12} />
              </button>
            ))}
            {hasPrice && (
              <button type="button" className={styles.tag} onClick={() => onPriceChange('', '')}
                aria-label={t('store.filters.removePrice')}>
                {priceTagText()} <CloseIcon size={12} />
              </button>
            )}
            {sortBy !== 'newest' && (
              <button type="button" className={styles.tag} onClick={() => onSortChange('newest')}
                aria-label={t('store.filters.removeSort')}>
                {t(`store.filters.sort_${sortBy}`)} <CloseIcon size={12} />
              </button>
            )}
            <button type="button" className={styles.clearAll} onClick={onClearFilters}>
              {t('store.filters.clearAll')}
            </button>
          </div>
        )}
      </div>
    </section>
  );
}
