import { useState, useEffect } from 'react';
import { api } from '../api/client';

// ============================================================================
// useProducts — Hook مخصّص يغلّف منطق جلب المنتجات (تحميل/بيانات/خطأ).
// لماذا؟ منطق "اجلب بيانات وتعامل مع التحميل والخطأ" يتكرّر كثيراً. نغلّفه مرة
// ونعيد استخدامه. المكوّن يصبح نظيفاً: يستدعي useProducts ويعرض النتيجة فقط.
// ============================================================================
// refreshKey: قيمة يبدّلها المستدعي ليجبر إعادة الجلب (مثلاً بعد إتمام طلب،
// كي تعكس الواجهة المخزون الجديد من الخادم).
export function useProducts({ keyword, categoryId, refreshKey } = {}) {
  const [products, setProducts] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);

  useEffect(() => {
    let active = true;
    setLoading(true);
    api.getProducts({ keyword, categoryId })
      .then((data) => { if (active) { setProducts(data.items); setError(null); } })
      .catch((e) => { if (active) setError(e.message); })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };   // تنظيف لتجنّب تحديث مكوّن أُزيل
  }, [keyword, categoryId, refreshKey]);

  return { products, loading, error };
}
