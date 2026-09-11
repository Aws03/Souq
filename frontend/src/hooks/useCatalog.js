import { useState, useEffect, useCallback } from 'react';
import { api } from '../api/client';

// ============================================================================
// useCatalog — يجلب صفحة كتالوج كاملةً (العناصر + إجمالي العدد
// والصفحات) لأن الكتالوج يحتاج العدّاد "عرض 12 / 58" والترقيم، بعكس صفوف
// الاكتشاف التي تكتفي بالعناصر. كل الفلاتر + الصفحة + حجم الصفحة كمُدخلات.
// ============================================================================
export function useCatalog({
  keyword, categoryIds, minPrice, maxPrice, sortBy, page = 1, pageSize = 12, refreshKey,
} = {}) {
  const [data, setData] = useState({ items: [], totalCount: 0, totalPages: 1, pageNumber: 1 });
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [retryTick, setRetryTick] = useState(0);

  // مصفوفة الفئات تصل بمرجع جديد كل رسم — نشتقّ مفتاحاً نصّياً ثابتاً كتبعية.
  const catsKey = (categoryIds || []).join(',');

  useEffect(() => {
    let active = true;
    setLoading(true);
    api.getProducts({ keyword, categoryIds, minPrice, maxPrice, sortBy, page, pageSize })
      .then((res) => { if (active) { setData(res); setError(null); } })
      .catch((e) => { if (active) setError(e.message); })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
    // eslint-disable-next-line react-hooks/exhaustive-deps -- categoryIds ممثَّلة بـ catsKey
  }, [keyword, catsKey, minPrice, maxPrice, sortBy, page, pageSize, refreshKey, retryTick]);

  const refetch = useCallback(() => setRetryTick((t) => t + 1), []);

  return { ...data, loading, error, refetch };
}
