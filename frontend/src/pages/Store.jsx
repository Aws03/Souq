import { useState, useEffect } from 'react';
import { useOutletContext } from 'react-router-dom';
import Storefront from './Storefront';
import { api } from '../api/client';
import { useProducts } from '../hooks/useProducts';
import { useDebouncedValue } from '../hooks/useDebouncedValue';

const HOME_SECTION_SIZE = 8;

// صفحة المتجر: تجلب المنتجات (من الـ API الحقيقي) والفئات، وتُمرّرها لعرض
// Storefront. refreshKey من تخطيط المتجر يُعيد الجلب بعد إتمام طلب (تحديث المخزون).
// searchTerm يأتي من شريط بحث شريط التنقّل (حالة مرفوعة في CustomerLayout).
export default function Store() {
  const { showToast, refreshKey, searchTerm } = useOutletContext();
  const [filter, setFilter] = useState(null);
  const [categories, setCategories] = useState([]);
  const debouncedSearch = useDebouncedValue(searchTerm, 300);

  const { products, loading, error, refetch } = useProducts({ keyword: debouncedSearch, categoryId: filter, refreshKey });

  const [newArrivals, setNewArrivals] = useState([]);
  const [newArrivalsLoading, setNewArrivalsLoading] = useState(true);
  const [bestSellers, setBestSellers] = useState([]);
  const [bestSellersLoading, setBestSellersLoading] = useState(true);

  useEffect(() => {
    api.getCategories().then(setCategories).catch(() => setCategories([]));
  }, []);

  // "وصل حديثاً": GET /api/products بلا كلمة بحث يُرتّب بالفعل بالأحدث أوّلاً
  // (Id تنازلياً في المستودع) — الصفحة الأولى هي فعلاً أحدث 8 منتجات.
  useEffect(() => {
    let active = true;
    setNewArrivalsLoading(true);
    api.getProducts({ page: 1, pageSize: HOME_SECTION_SIZE })
      .then((res) => { if (active) setNewArrivals(res.items); })
      .catch(() => { if (active) setNewArrivals([]); })
      .finally(() => { if (active) setNewArrivalsLoading(false); });
    return () => { active = false; };
  }, [refreshKey]);

  // "الأكثر مبيعاً": لا حقل عدّاد مبيعات لكل منتج في الـ API الحالي، فلا يوجد
  // ترتيب حقيقي بالمبيعات ممكن هنا. نعرض دفعة ثانية حقيقية من الكتالوج (لا
  // بيانات مُختلقة) بدل تكرار "وصل حديثاً" بصرياً، مع رجوع لعكس نفس القائمة
  // إن كان الكتالوج أصغر من صفحتين — إلى أن يُضاف ترتيب مبيعات حقيقي بالخادم.
  useEffect(() => {
    let active = true;
    setBestSellersLoading(true);
    api.getProducts({ page: 2, pageSize: HOME_SECTION_SIZE })
      .then((res) => {
        if (!active) return;
        setBestSellers(res.items.length ? res.items : [...newArrivals].reverse());
      })
      .catch(() => { if (active) setBestSellers([]); })
      .finally(() => { if (active) setBestSellersLoading(false); });
    return () => { active = false; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [refreshKey, newArrivals]);

  return (
    <Storefront
      products={products} categories={categories} loading={loading} error={error} onRetry={refetch}
      filter={filter} setFilter={setFilter} onAdded={showToast}
      newArrivals={newArrivals} newArrivalsLoading={newArrivalsLoading}
      bestSellers={bestSellers} bestSellersLoading={bestSellersLoading}
    />
  );
}
