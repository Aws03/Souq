import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api } from '../api/client';
import { useAuth } from './AuthContext';
import { useToast } from './ToastContext';
import { EMPTY_BASKET, nextQuantity, toCartItems } from '../features/basket/basketModel';

// ============================================================================
// CartContext — السلة من الخادم (المرحلة 8، ADR-0028). الخادم مصدر الحقيقة للأصناف والأسعار والمجاميع (خطّ التسعير نفسه
// الذي يُنشئ الطلب)، فتنجو السلة من التحديث وتتبع العميل بين أجهزته (C12). الزائر يُعرَّف بملف تعريف ارتباط HttpOnly
// يضعه الخادم. كل عملية تعيد السلة كاملة فتُستبدل الحالة بها — لا حساب محلي يمكن أن يختلف عن الخادم.
// ============================================================================
const CartContext = createContext();

export function CartProvider({ children }) {
  const { i18n } = useTranslation();
  const lang = i18n.language?.startsWith('en') ? 'en' : 'ar';
  const { user, loading: sessionLoading } = useAuth();
  const toast = useToast();
  const [basket, setBasket] = useState(EMPTY_BASKET);
  const [loaded, setLoaded] = useState(false);
  const [loadFailed, setLoadFailed] = useState(false);

  // ==========================================================================
  // قراءةٌ فاشلة ليست سلّةً فارغة.
  //
  // كان الجسم `.catch(() => {}).finally(() => setLoaded(true))`: أي إخفاق في قراءة
  // السلّة يُبتلع، فتبقى `EMPTY_BASKET` وتُعلَن **محمَّلة**. والشاشات تقرأ ذلك كما هو
  // مكتوب: صفحة الدفع تعرض «أضف منتجات أولاً» لمشترٍ سلّته على الخادم فيها أصنافه.
  // قِيس: بإفشال قراءة واحدة لـ GET /api/basket بعد أن تُظهر شارة السلّة 1، تُعلن صفحة
  // الدفع السلّة فارغة ويختفي حقل العنوان — وهو بعينه إخفاق `checkout-reliability`
  // المتقطّع تحت الحِمل (TD-57): لا عيب في الرحلة، بل في ما تراه.
  //
  // وهو قبل ذلك عطلٌ في المنتج: زبونٌ تُخبره أن سلّته فارغة يُعيد الشراء أو ينصرف،
  // وأصنافه سليمة طوال الوقت.
  //
  // قراءةٌ واحدة تُعاد ثمّ يُصارَح: القراءة عديمة الأثر (idempotent) فإعادتها آمنة،
  // وأغلب الإخفاقات هنا عابرة. وإن أصرّ الإخفاق فالحالة تُعلَن ومعها إعادة محاولة —
  // لا صمتٌ يُقرأ فراغاً.
  // ==========================================================================
  const reload = useCallback(async () => {
    for (let attempt = 0; attempt < 2; attempt += 1) {
      try {
        setBasket(await api.getBasket());
        setLoadFailed(false);
        return;
      } catch {
        if (attempt === 0) await new Promise((resolve) => { setTimeout(resolve, 400); });
      } finally {
        setLoaded(true);
      }
    }
    setLoadFailed(true);
  }, []);

  // بعد استعادة الجلسة لا قبلها، وعند كل تبدّل للمستخدم: أول قراءة بعد الدخول يدمج فيها الخادم سلة الزائر.
  useEffect(() => {
    if (!sessionLoading) reload();
  }, [sessionLoading, user?.id, reload]);

  // الخطأ (نفاد المتاح، منتج لم يعد متاحاً) يُعرض هنا مرة واحدة؛ المستدعي يعرف النجاح من القيمة المعادة.
  const run = useCallback(async (operation) => {
    try {
      setBasket(await operation());
      return true;
    } catch (e) {
      toast.error(e.message);
      return false;
    }
  }, [toast]);

  // وصف المتغيّر يتبع لغة الواجهة فوراً عند تبديلها (الخادم يرسله بكل لغات المنتج).
  const items = useMemo(() => toCartItems(basket, lang), [basket, lang]);

  const value = useMemo(() => {
    // السطر يُعرَّف بمتغيّره لا بمنتجه (V3): منتج واحد بمقاسين سطران، ومسارات المنتج تردّ VariantRequired عليهما.
    const change = (variantId, delta) => {
      const item = items.find((i) => i.variantId === variantId);
      return item ? run(() => api.setBasketVariantQuantity(variantId, nextQuantity(item, delta))) : Promise.resolve(false);
    };
    return {
      basket, items, loaded, loadFailed,
      total: basket.subtotal,
      count: basket.itemCount,
      add: (product, quantity = 1, variantId = null) => run(() => api.addToBasket(product.id, quantity, variantId)),
      inc: (variantId) => change(variantId, 1),
      dec: (variantId) => change(variantId, -1),
      remove: (variantId) => run(() => api.removeBasketVariant(variantId)),
      reload,
    };
  }, [basket, items, loaded, loadFailed, run, reload]);

  return <CartContext.Provider value={value}>{children}</CartContext.Provider>;
}

// Hook مخصّص: أي مكوّن ينادي useCart() ويحصل على السلة فوراً.
export const useCart = () => useContext(CartContext);
