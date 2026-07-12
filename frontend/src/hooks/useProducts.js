import { useState, useEffect, useCallback } from 'react';
import { api } from '../api/client';

// ============================================================================
// useProducts — Hook مخصّص يغلّف منطق جلب المنتجات (تحميل/بيانات/خطأ).
// لماذا؟ منطق "اجلب بيانات وتعامل مع التحميل والخطأ" يتكرّر كثيراً. نغلّفه مرة
// ونعيد استخدامه. المكوّن يصبح نظيفاً: يستدعي useProducts ويعرض النتيجة فقط.
// ============================================================================
// refreshKey: قيمة يبدّلها المستدعي ليجبر إعادة الجلب (مثلاً بعد إتمام طلب،
// كي تعكس الواجهة المخزون الجديد من الخادم). refetch: إعادة محاولة يدوية بعد خطأ.
export function useProducts({ keyword, categoryIds, minPrice, maxPrice, sortBy, refreshKey } = {}) {
  const [products, setProducts] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [retryTick, setRetryTick] = useState(0);

  // مصفوفة الفئات تصل بمرجع جديد كل رسم — نشتقّ تمثيلاً نصّياً ثابتاً كتبعية
  // للتأثير كي لا يُعاد الجلب إلا حين يتغيّر المحتوى فعلاً.
  const catsKey = (categoryIds || []).join(',');

  useEffect(() => {
    let active = true;
    setLoading(true);
    api.getProducts({ keyword, categoryIds, minPrice, maxPrice, sortBy })
      .then((data) => { if (active) { setProducts(data.items); setError(null); } })
      .catch((e) => { if (active) setError(e.message); })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };   // تنظيف لتجنّب تحديث مكوّن أُزيل
    // eslint-disable-next-line react-hooks/exhaustive-deps -- categoryIds ممثَّلة بـ catsKey
  }, [keyword, catsKey, minPrice, maxPrice, sortBy, refreshKey, retryTick]);

  const refetch = useCallback(() => setRetryTick((t) => t + 1), []);

  return { products, loading, error, refetch };
}
