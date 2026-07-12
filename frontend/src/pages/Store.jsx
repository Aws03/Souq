import { useState, useEffect } from 'react';
import { useOutletContext } from 'react-router-dom';
import Storefront from './Storefront';
import { api } from '../api/client';

const HOME_SECTION_SIZE = 10;

// صفحة المتجر الرئيسية: تجلب صفوف الاكتشاف (أحدث/أكثر مبيعاً/عروض) وتُمرّرها
// لعرض Storefront مع الفئات (من تخطيط المتجر). الكتالوج نفسه يجلب صفحته بنفسه
// (Catalog) اعتماداً على فلاتر الرابط. refreshKey يُعيد الجلب بعد إتمام طلب.
export default function Store() {
  const { showToast, refreshKey, searchTerm, categories } = useOutletContext();

  const [newArrivals, setNewArrivals] = useState([]);
  const [newArrivalsLoading, setNewArrivalsLoading] = useState(true);
  const [bestSellers, setBestSellers] = useState([]);
  const [bestSellersLoading, setBestSellersLoading] = useState(true);
  const [offers, setOffers] = useState([]);
  const [offersLoading, setOffersLoading] = useState(true);

  // "وصل حديثاً": الترتيب الافتراضي (الأحدث أولاً).
  useEffect(() => {
    let active = true;
    setNewArrivalsLoading(true);
    api.getProducts({ page: 1, pageSize: HOME_SECTION_SIZE })
      .then((res) => { if (active) setNewArrivals(res.items); })
      .catch(() => { if (active) setNewArrivals([]); })
      .finally(() => { if (active) setNewArrivalsLoading(false); });
    return () => { active = false; };
  }, [refreshKey]);

  // "الأكثر مبيعاً": مجموع الكميات عبر الطلبات المُسلَّمة (ترتيب الخادم الحقيقي).
  useEffect(() => {
    let active = true;
    setBestSellersLoading(true);
    api.getProducts({ page: 1, pageSize: HOME_SECTION_SIZE, sortBy: 'BestSelling' })
      .then((res) => { if (active) setBestSellers(res.items); })
      .catch(() => { if (active) setBestSellers([]); })
      .finally(() => { if (active) setBestSellersLoading(false); });
    return () => { active = false; };
  }, [refreshKey]);

  // "العروض" (مؤقّت): كل المنتجات النشطة بترتيب الأحدث حتى تُضاف راية عرض حقيقية
  // للمنتجات — نعرض دفعة ثانية كي لا يطابق صفّها صفّ "وصل حديثاً" بصرياً.
  useEffect(() => {
    let active = true;
    setOffersLoading(true);
    api.getProducts({ page: 2, pageSize: HOME_SECTION_SIZE })
      .then((res) => { if (active) setOffers(res.items.length ? res.items : []); })
      .catch(() => { if (active) setOffers([]); })
      .finally(() => { if (active) setOffersLoading(false); });
    return () => { active = false; };
  }, [refreshKey]);

  return (
    <Storefront
      categories={categories} onAdded={showToast} searchTerm={searchTerm} refreshKey={refreshKey}
      newArrivals={newArrivals} newArrivalsLoading={newArrivalsLoading}
      bestSellers={bestSellers} bestSellersLoading={bestSellersLoading}
      offers={offers} offersLoading={offersLoading}
    />
  );
}
