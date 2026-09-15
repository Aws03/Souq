import { useOutletContext } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import Storefront from './Storefront';
import { api } from '../api/client';
import { queryKeys } from '../app/queryKeys';
import { usePageMetadata } from '../app/usePageMetadata';

const HOME_SECTION_SIZE = 10;

// صفحة المتجر الرئيسية: تجلب صفوف الاكتشاف (أحدث/أكثر مبيعاً/عروض) وتُمرّرها لعرض Storefront مع
// الفئات (من تخطيط المتجر). الكتالوج نفسه يجلب صفحته بنفسه (Catalog) اعتماداً على فلاتر الرابط.
// refreshKey يُعيد الجلب بعد إتمام طلب — المخزون تغيّر على الخادم.
//
// الصفوف الثلاثة استعلامات مستقلّة بمفاتيحها: صفٌّ يفشل لا يُفرغ الصفحة، والرجوع إلى الرئيسية
// يعرضها من الذاكرة بدل ثلاثة هياكل عظمية لبيانات وصلت قبل ثوانٍ.
function useHomeRow(params, refreshKey) {
  return useQuery({
    queryKey: queryKeys.products({ ...params, pageSize: HOME_SECTION_SIZE, refreshKey }),
    queryFn: () => api.getProducts({ ...params, page: 1, pageSize: HOME_SECTION_SIZE }),
    // صفّ اكتشاف فشل جلبه يُخفى (ProductSection يُخفي الفارغ) بدل لافتة خطأ في الرئيسية.
    retry: false,
  });
}

export default function Store() {
  // الرئيسية تحمل هوية المتجر نفسها؛ الخطّاف يضيف الرابط القانوني ووسوم المشاركة.
  usePageMetadata();
  const { showToast, refreshKey, categories } = useOutletContext();

  // "وصل حديثاً" الترتيب الافتراضي؛ "الأكثر مبيعاً" مجموع الكميات عبر الطلبات المُسلَّمة؛
  // و"العروض" ما عليه تخفيض فعلاً (onSale) — لا "الصفحة الثانية من الأحدث" باسم عروض.
  const newArrivals = useHomeRow({}, refreshKey);
  const bestSellers = useHomeRow({ sortBy: 'BestSelling' }, refreshKey);
  const offers = useHomeRow({ onSale: true }, refreshKey);

  return (
    <Storefront
      categories={categories} onAdded={showToast} refreshKey={refreshKey}
      newArrivals={newArrivals.data?.items ?? []} newArrivalsLoading={newArrivals.isPending}
      bestSellers={bestSellers.data?.items ?? []} bestSellersLoading={bestSellers.isPending}
      offers={offers.data?.items ?? []} offersLoading={offers.isPending}
    />
  );
}
