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

  const reload = useCallback(
    () => api.getBasket().then(setBasket).catch(() => {}).finally(() => setLoaded(true)),
    []);

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
      basket, items, loaded,
      total: basket.subtotal,
      count: basket.itemCount,
      add: (product, quantity = 1, variantId = null) => run(() => api.addToBasket(product.id, quantity, variantId)),
      inc: (variantId) => change(variantId, 1),
      dec: (variantId) => change(variantId, -1),
      remove: (variantId) => run(() => api.removeBasketVariant(variantId)),
      reload,
    };
  }, [basket, items, loaded, run, reload]);

  return <CartContext.Provider value={value}>{children}</CartContext.Provider>;
}

// Hook مخصّص: أي مكوّن ينادي useCart() ويحصل على السلة فوراً.
export const useCart = () => useContext(CartContext);
