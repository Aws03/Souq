import { useMemo, useState, useEffect, useCallback } from 'react';
import { useSearchParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useCatalog } from '../../hooks/useCatalog';
import ProductGrid from '../product/ProductGrid';
import Pagination from '../common/Pagination';
import { formatPrice, getCategoryName } from '../product/ProductBadges';
import { Cols5Icon, Cols4Icon, RowsIcon, CloseIcon } from '../icons/Icons';
import styles from './Catalog.module.css';

// relevance متاح حين توجد كلمة بحث وحدها: بلا كلمة لا معنى لدرجة مطابقة (والخادم يعيدها إلى الأحدث).
const SORT_API = { relevance: 'Relevance', newest: 'Newest', priceAsc: 'PriceAsc', priceDesc: 'PriceDesc' };
const SORT_KEYS = ['newest', 'priceAsc', 'priceDesc'];
const SEARCH_SORT_KEYS = ['relevance', ...SORT_KEYS];
const VIEWS = [
  { key: 'grid5', Icon: Cols5Icon },
  { key: 'grid4', Icon: Cols4Icon },
  { key: 'list', Icon: RowsIcon },
];
const PAGE_SIZE = 12;

// ============================================================================
// الكتالوج القابل لإعادة الاستخدام (المتجر الرئيسي + صفحة العروض): شريط أدوات
// (عدّاد النتائج + أزرار ترتيب + نطاق سعر بتطبيق/مسح + تبديل عرض 5/4/قائمة)،
// وسوم الفلاتر المفعّلة القابلة للإزالة، شبكة المنتجات، وترقيم أسفلها.
// كل الحالة في رابط الصفحة (?cats=&min=&max=&sort=&view=&page=) — قابلة للمشاركة.
// lockSort يثبّت الترتيب ويخفي أزراره (تستخدمه صفحة العروض: الأحدث دائماً).
// ============================================================================
export default function Catalog({ categories = [], onAdded, refreshKey, lockSort, onSale = false }) {
  const { t } = useTranslation();
  const [searchParams, setSearchParams] = useSearchParams();
  // البحث من الرابط: نتيجة قابلة للمشاركة، وزرّ الرجوع يعيدها (searchRouting).
  // لا تأخير ثانٍ هنا: useStoreSearch يؤخّر الكتابة قبل أن يكتب `q` في الرابط أصلاً، وتأخيرٌ إضافي كان
  // يجعل الحرف يستغرق ~600ms ليصل الشبكة بدل ~300ms — تأخير مضاعف بلا فائدة (وُجد في تدقيق M3).
  const keyword = searchParams.get('q') ?? '';

  const categoryIds = useMemo(
    () => (searchParams.get('cats') || '').split(',').map(Number).filter((n) => Number.isInteger(n) && n > 0),
    [searchParams]
  );
  const minPrice = searchParams.get('min') || '';
  const maxPrice = searchParams.get('max') || '';
  const rawSort = searchParams.get('sort');
  // مع كلمة بحث يكون الافتراضي "الأكثر مطابقةً": الأحدث أولاً في نتيجة بحث يدفن التطابق التامّ.
  const searching = keyword.trim() !== '';
  const defaultSort = searching ? 'relevance' : 'newest';
  const sortKeys = searching ? SEARCH_SORT_KEYS : SORT_KEYS;
  const sortBy = lockSort || (sortKeys.includes(rawSort) ? rawSort : defaultSort);
  const view = ['grid5', 'grid4', 'list'].includes(searchParams.get('view')) ? searchParams.get('view') : 'grid5';
  // ?exact=1 — المتسوّق رفض التصحيح المعروض وأصرّ على كلماته (M3).
  const exact = searchParams.get('exact') === '1';
  const page = Math.max(1, parseInt(searchParams.get('page'), 10) || 1);

  // تعديل موضعي للرابط. resetPage: أي تغيير في الفلاتر يعيد للصفحة 1 (نتائج
  // مختلفة كلياً)، بينما تبديل العرض/تصفّح الصفحات لا يعيدها.
  const updateParams = useCallback((patch, { resetPage = true } = {}) => {
    setSearchParams((prev) => {
      const next = new URLSearchParams(prev);
      const full = resetPage ? { page: null, ...patch } : patch;
      Object.entries(full).forEach(([k, v]) => {
        if (v == null || v === '' || (Array.isArray(v) && v.length === 0)) next.delete(k);
        else next.set(k, Array.isArray(v) ? v.join(',') : v);
      });
      return next;
    }, { replace: true });
  }, [setSearchParams]);

  const removeCategory = (id) => updateParams({ cats: categoryIds.filter((c) => c !== id) });
  const setSort = (key) => updateParams({ sort: key === defaultSort ? null : key });
  const setView = (v) => updateParams({ view: v === 'grid5' ? null : v }, { resetPage: false });
  const setPage = (p) => updateParams({ page: p <= 1 ? null : p }, { resetPage: false });
  const clearFilters = () => updateParams({ cats: null, min: null, max: null, sort: null, exact: null });

  // مسودة السعر محلية كي لا يُطلق طلباً مع كل رقم — تُعتمد عند "تطبيق" أو Enter،
  // وتُزامَن حين يتغيّر الرابط خارجياً (إزالة وسم السعر مثلاً).
  const [priceDraft, setPriceDraft] = useState({ min: minPrice, max: maxPrice });
  useEffect(() => { setPriceDraft({ min: minPrice, max: maxPrice }); }, [minPrice, maxPrice]);
  const applyPrice = () => {
    const clean = (v) => (v === '' || Number(v) < 0 || Number.isNaN(Number(v)) ? '' : v);
    updateParams({ min: clean(priceDraft.min), max: clean(priceDraft.max) });
  };
  const clearPrice = () => { setPriceDraft({ min: '', max: '' }); updateParams({ min: null, max: null }); };

  const { items, totalCount, totalPages, loading, error, refetch, search } = useCatalog({
    keyword, categoryIds, minPrice, maxPrice, onSale, exact,
    sortBy: SORT_API[sortBy], page, pageSize: PAGE_SIZE, refreshKey,
  });

  const activeCats = categories.filter((c) => categoryIds.includes(c.id));
  const hasPrice = minPrice !== '' || maxPrice !== '';
  const showSortTag = sortBy !== defaultSort && !lockSort;
  const hasFilters = activeCats.length > 0 || hasPrice || showSortTag;

  const priceTagText = minPrice !== '' && maxPrice !== ''
    ? `${formatPrice(minPrice)} – ${formatPrice(maxPrice)}`
    : minPrice !== '' ? `≥ ${formatPrice(minPrice)}` : `≤ ${formatPrice(maxPrice)}`;

  const resultsCount = t('catalog.resultsCount', { shown: items.length, total: totalCount });

  return (
    <div className={styles.catalog}>
      <div className={styles.toolbar}>
        <span className={styles.count}>{resultsCount}</span>

        <div className={styles.controls}>
          {!lockSort && (
            <div className={styles.sortGroup} role="group" aria-label={t('store.filters.sort')}>
              {sortKeys.map((k) => (
                <button key={k} type="button" aria-pressed={sortBy === k}
                  className={`${styles.sortBtn} ${sortBy === k ? styles.sortActive : ''}`}
                  onClick={() => setSort(k)}>
                  {t(`store.filters.sort_${k}`)}
                </button>
              ))}
            </div>
          )}

          <div className={styles.price}>
            <input type="number" min="0" inputMode="decimal" placeholder={t('store.filters.min')}
              aria-label={t('store.filters.minAria')} value={priceDraft.min}
              onChange={(e) => setPriceDraft((d) => ({ ...d, min: e.target.value }))}
              onKeyDown={(e) => { if (e.key === 'Enter') applyPrice(); }} />
            <input type="number" min="0" inputMode="decimal" placeholder={t('store.filters.max')}
              aria-label={t('store.filters.maxAria')} value={priceDraft.max}
              onChange={(e) => setPriceDraft((d) => ({ ...d, max: e.target.value }))}
              onKeyDown={(e) => { if (e.key === 'Enter') applyPrice(); }} />
            <button type="button" className={styles.applyBtn} onClick={applyPrice}>{t('catalog.apply')}</button>
            <button type="button" className={styles.clearBtn} onClick={clearPrice}>{t('catalog.clear')}</button>
          </div>

          <div className={styles.views} role="group" aria-label={t('catalog.viewAria')}>
            {VIEWS.map(({ key, Icon }) => (
              <button key={key} type="button" aria-pressed={view === key}
                className={`${styles.viewBtn} ${view === key ? styles.viewActive : ''}`}
                onClick={() => setView(key)} aria-label={t(`catalog.view_${key}`)} title={t(`catalog.view_${key}`)}>
                <Icon size={18} />
              </button>
            ))}
          </div>
        </div>
      </div>

      {hasFilters && (
        <div className={styles.tags}>
          {activeCats.map((c) => (
            <button key={c.id} type="button" className={styles.tag}
              onClick={() => removeCategory(c.id)} aria-label={t('store.filters.remove', { name: getCategoryName(c) })}>
              {getCategoryName(c)} <CloseIcon size={12} />
            </button>
          ))}
          {hasPrice && (
            <button type="button" className={styles.tag} onClick={clearPrice} aria-label={t('store.filters.removePrice')}>
              {priceTagText} <CloseIcon size={12} />
            </button>
          )}
          {showSortTag && (
            <button type="button" className={styles.tag} onClick={() => setSort(defaultSort)} aria-label={t('store.filters.removeSort')}>
              {t(`store.filters.sort_${sortBy}`)} <CloseIcon size={12} />
            </button>
          )}
          <button type="button" className={styles.clearAll} onClick={clearFilters}>{t('store.filters.clearAll')}</button>
        </div>
      )}

      {/* ============================================================================
          استرجاع البحث (M3، ADR-0042): يُعرض فقط حين يقول الخادم إنّه فعل شيئاً — لا تصحيح تخترعه الواجهة.
          الكلمات تمرّ بمُنسِّق bidi (U+2068/U+2069) في ملفّ الترجمة كي لا تنقلب جملة عربية تحمل كلمة لاتينية.
          ولأنّ الشبكة تتغيّر تحت المتسوّق، اللافتة role="status" فيسمعها قارئ الشاشة بلا مقاطعة.
          ============================================================================ */}
      {!loading && search?.searchedInstead && (
        <div className={styles.recovery} role="status">
          <span>
            {t('catalog.search.showingInstead', { searched: search.searchedInstead, original: search.term })}
          </span>
          {/* الإصرار على الكلمة الأصلية: زيارة صريحة تُعيد البحث بها وتعطي الحالة الفارغة بلا تصحيح. */}
          <button type="button" className={styles.recoveryLink}
            onClick={() => updateParams({ exact: '1' })}>
            {t('catalog.search.searchOriginalInstead', { original: search.term })}
          </button>
        </div>
      )}

      <ProductGrid
        products={items} loading={loading} error={error} onRetry={refetch} onAdded={onAdded} view={view}
        empty={search?.category ? {
          title: t('catalog.search.noMatchTitle', { term: search.term }),
          message: t('catalog.search.categorySuggestion', { name: search.category.name }),
          actionLabel: t('catalog.search.browseCategory', { name: search.category.name }),
          onAction: () => updateParams({ q: null, cats: [search.category.id] }),
        } : undefined}
      />

      <div className={styles.pager}>
        {!loading && totalCount > 0 && <span className={styles.pagerCount}>{resultsCount}</span>}
        <Pagination page={page} totalPages={totalPages} onChange={setPage} />
      </div>
    </div>
  );
}
