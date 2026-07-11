import { useState, useEffect } from 'react';
import { useOutletContext } from 'react-router-dom';
import Storefront from './Storefront';
import { api } from '../api/client';
import { useProducts } from '../hooks/useProducts';

// صفحة المتجر: تجلب المنتجات (من الـ API الحقيقي) والفئات، وتُمرّرها لعرض
// Storefront. refreshKey من تخطيط المتجر يُعيد الجلب بعد إتمام طلب (تحديث المخزون).
export default function Store() {
  const { showToast, refreshKey } = useOutletContext();
  const [filter, setFilter] = useState(null);
  const [categories, setCategories] = useState([]);

  const { products, loading, error } = useProducts({ categoryId: filter, refreshKey });

  useEffect(() => {
    api.getCategories().then(setCategories).catch(() => setCategories([]));
  }, []);

  return (
    <Storefront products={products} categories={categories} loading={loading} error={error}
      filter={filter} setFilter={setFilter} onAdded={showToast} />
  );
}
