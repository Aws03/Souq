import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { api } from '../api/client';
import { queryKeys } from '../app/queryKeys';

// ============================================================================
// useCatalog — صفحة كتالوج كاملة (العناصر + إجمالي العدد والصفحات) لأن الكتالوج يحتاج العدّاد
// "عرض 12 / 58" والترقيم، بعكس صفوف الاكتشاف التي تكتفي بالعناصر.
//
// المرحلة 16 (ADR-0037): الجلب لطبقة الاستعلام. ما تغيّر للزائر:
//   • keepPreviousData — الانتقال إلى الصفحة التالية أو تغيير مرشّح يُبقي الشبكة الحالية معروضة
//     ويستبدلها عند وصول الجديدة، بدل ومضة "لا نتائج" بين الاثنتين.
//   • الرجوع بالمتصفّح إلى نفس المرشّحات يعرض النتيجة فوراً ثم يتحقّق منها خلفه.
//   • حارس الإلغاء المكتوب بيد لم يعد ضرورياً: ردّ استعلام قديم لا يُكتب فوق الحالي (TD-25).
//
// refreshKey يبقى: إضافة إلى السلّة تُغيّر المتاح على الخادم، فتُبطِل الشبكة المعروضة.
// ============================================================================
const EMPTY_PAGE = { items: [], totalCount: 0, totalPages: 1, pageNumber: 1 };

/**
 * @param {{keyword?: string, categoryIds?: number[], minPrice?: number, maxPrice?: number,
 *          sortBy?: string, page?: number, pageSize?: number, refreshKey?: number,
 *          onSale?: boolean}} [filters]
 */
export function useCatalog({
  keyword, categoryIds, minPrice, maxPrice, sortBy, page = 1, pageSize = 12, refreshKey, onSale = false,
} = {}) {
  const params = {
    keyword, categoryIds: (categoryIds || []).join(','), minPrice, maxPrice, sortBy, page, pageSize, onSale, refreshKey,
  };

  const query = useQuery({
    queryKey: queryKeys.products(params),
    queryFn: () => api.getProducts({ keyword, categoryIds, minPrice, maxPrice, sortBy, page, pageSize, onSale }),
    placeholderData: keepPreviousData,
  });

  return {
    ...(query.data ?? EMPTY_PAGE),
    loading: query.isPending,
    error: query.error?.message ?? null,
    refetch: query.refetch,
  };
}
