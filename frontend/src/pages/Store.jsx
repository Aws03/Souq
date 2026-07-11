import { useState, useEffect } from 'react';
import { useOutletContext } from 'react-router-dom';
import Storefront from './Storefront';
import { api } from '../api/client';
import { useProducts } from '../hooks/useProducts';
import { useDebouncedValue } from '../hooks/useDebouncedValue';

// صفحة المتجر: تجلب المنتجات (من الـ API الحقيقي) والفئات، وتُمرّرها لعرض
// Storefront. refreshKey من تخطيط المتجر يُعيد الجلب بعد إتمام طلب (تحديث المخزون).
// searchTerm يأتي من شريط بحث شريط التنقّل (حالة مرفوعة في CustomerLayout).
export default function Store() {
  const { showToast, refreshKey, searchTerm } = useOutletContext();
  const [filter, setFilter] = useState(null);
  const [categories, setCategories] = useState([]);
  const debouncedSearch = useDebouncedValue(searchTerm, 300);

  const { products, loading, error, refetch } = useProducts({ keyword: debouncedSearch, categoryId: filter, refreshKey });

  useEffect(() => {
    api.getCategories().then(setCategories).catch(() => setCategories([]));
  }, []);

  return (
    <Storefront products={products} categories={categories} loading={loading} error={error} onRetry={refetch}
      filter={filter} setFilter={setFilter} onAdded={showToast} />
  );
}
