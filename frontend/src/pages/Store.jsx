import { useState, useEffect, useMemo, useCallback } from 'react';
import { useOutletContext, useSearchParams } from 'react-router-dom';
import Storefront from './Storefront';
import { api } from '../api/client';
import { useProducts } from '../hooks/useProducts';
import { useDebouncedValue } from '../hooks/useDebouncedValue';

const HOME_SECTION_SIZE = 8;

// مفاتيح الترتيب في الرابط (قصيرة وقابلة للمشاركة) → قيم enum في الـ API.
const SORT_API = { newest: 'Newest', priceAsc: 'PriceAsc', priceDesc: 'PriceDesc', bestSelling: 'BestSelling' };

// صفحة المتجر: تجلب المنتجات (من الـ API الحقيقي) والفئات، وتُمرّرها لعرض
// Storefront. refreshKey من تخطيط المتجر يُعيد الجلب بعد إتمام طلب (تحديث المخزون).
// searchTerm يأتي من شريط بحث شريط التنقّل (حالة مرفوعة في CustomerLayout).
export default function Store() {
  const { showToast, refreshKey, searchTerm } = useOutletContext();
  const [categories, setCategories] = useState([]);
  const debouncedSearch = useDebouncedValue(searchTerm, 300);

  // فلاتر الكتالوج تعيش في رابط الصفحة (?cats=1,2&min=10&max=99&sort=priceAsc)
  // — روابط قابلة للمشاركة وتنجو من تحديث الصفحة، بعكس حالة محلية تضيع.
  const [searchParams, setSearchParams] = useSearchParams();
  const categoryIds = useMemo(
    () => (searchParams.get('cats') || '').split(',').map(Number)
      .filter((n) => Number.isInteger(n) && n > 0),
    [searchParams]
  );
  const minPrice = searchParams.get('min') || '';
  const maxPrice = searchParams.get('max') || '';
  const rawSort = searchParams.get('sort');
  const sortBy = SORT_API[rawSort] ? rawSort : 'newest';   // قيمة غريبة بالرابط ⇒ الافتراضي

  // تعديل موضعي لمفاتيح الرابط: null/فارغ يحذف المفتاح (لا مفاتيح فارغة معلّقة)،
  // وreplace كي لا يتحوّل كل نقرة فلتر إلى خطوة رجوع في تاريخ المتصفّح.
  const updateParams = useCallback((patch) => {
    setSearchParams((prev) => {
      const next = new URLSearchParams(prev);
      Object.entries(patch).forEach(([key, value]) => {
        if (value == null || value === '' || (Array.isArray(value) && value.length === 0)) next.delete(key);
        else next.set(key, Array.isArray(value) ? value.join(',') : value);
      });
      return next;
    }, { replace: true });
  }, [setSearchParams]);

  // id فارغ (شريحة "الكل") يمسح اختيار الفئات كلّه؛ غير ذلك تبديل عضوية الفئة.
  const toggleCategory = (id) => {
    if (id == null) return updateParams({ cats: null });
    updateParams({
      cats: categoryIds.includes(id) ? categoryIds.filter((c) => c !== id) : [...categoryIds, id],
    });
  };
  // بطاقات الفئات في أعلى الصفحة: اختيار مفرد يستبدل التحديد (سلوك "تسوّق هذه الفئة").
  const selectCategory = (id) => updateParams({ cats: id == null ? null : [id] });
  const setPriceRange = (min, max) => updateParams({ min, max });
  const setSort = (key) => updateParams({ sort: key === 'newest' ? null : key });
  const clearFilters = () => updateParams({ cats: null, min: null, max: null, sort: null });

  const { products, loading, error, refetch } = useProducts({
    keyword: debouncedSearch, categoryIds, minPrice, maxPrice,
    sortBy: SORT_API[sortBy], refreshKey,
  });

  const [newArrivals, setNewArrivals] = useState([]);
  const [newArrivalsLoading, setNewArrivalsLoading] = useState(true);
  const [bestSellers, setBestSellers] = useState([]);
  const [bestSellersLoading, setBestSellersLoading] = useState(true);

  useEffect(() => {
    api.getCategories().then(setCategories).catch(() => setCategories([]));
  }, []);

  // "وصل حديثاً": الترتيب الافتراضي (الأحدث أولاً) — الصفحة الأولى هي أحدث 8 منتجات.
  useEffect(() => {
    let active = true;
    setNewArrivalsLoading(true);
    api.getProducts({ page: 1, pageSize: HOME_SECTION_SIZE })
      .then((res) => { if (active) setNewArrivals(res.items); })
      .catch(() => { if (active) setNewArrivals([]); })
      .finally(() => { if (active) setNewArrivalsLoading(false); });
    return () => { active = false; };
  }, [refreshKey]);

  // "الأكثر مبيعاً" حقيقي الآن: الخادم يرتّب بمجموع الكميات عبر الطلبات
  // المُسلَّمة (sortBy=BestSelling) — لا دفعة كتالوج بديلة كما كان قبل وجوده.
  useEffect(() => {
    let active = true;
    setBestSellersLoading(true);
    api.getProducts({ page: 1, pageSize: HOME_SECTION_SIZE, sortBy: 'BestSelling' })
      .then((res) => { if (active) setBestSellers(res.items); })
      .catch(() => { if (active) setBestSellers([]); })
      .finally(() => { if (active) setBestSellersLoading(false); });
    return () => { active = false; };
  }, [refreshKey]);

  return (
    <Storefront
      products={products} categories={categories} loading={loading} error={error} onRetry={refetch}
      filters={{ categoryIds, minPrice, maxPrice, sortBy }}
      onToggleCategory={toggleCategory} onSelectCategory={selectCategory}
      onPriceChange={setPriceRange} onSortChange={setSort} onClearFilters={clearFilters}
      onAdded={showToast}
      newArrivals={newArrivals} newArrivalsLoading={newArrivalsLoading}
      bestSellers={bestSellers} bestSellersLoading={bestSellersLoading}
    />
  );
}
