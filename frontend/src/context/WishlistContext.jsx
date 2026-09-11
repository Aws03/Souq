import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react';
import { api } from '../api/client';
import { useAuth } from './AuthContext';
import { useToast } from './ToastContext';
import {
  WISHLIST_STORAGE_KEY, localIds, parseStored, serverUnavailable, toggleLocal,
} from '../features/wishlist/wishlistModel';

// ============================================================================
// WishlistContext — المفضّلة (المرحلة 13، ADR-0033). الزائر: في localStorage (كائن المنتج كاملاً فتُعرض الصفحة بلا شبكة).
// العميل: على الخادم — تتبعه بين أجهزته بأسعار الكتالوج الحيّة؛ عند دخوله تُدمج القائمة المحلية في حسابه ثم تُفرَّغ، فلا يرثها
// زائر لاحق على الجهاز نفسه. وحدة معطّلة في المتجر أو حساب إدارة ⇒ القائمة المحلية وحدها كما قبل المرحلة 13.
// ============================================================================
const WishlistContext = createContext();

function readLocal() {
  try { return parseStored(localStorage.getItem(WISHLIST_STORAGE_KEY)); } catch { return []; }
}

function writeLocal(items) {
  try { localStorage.setItem(WISHLIST_STORAGE_KEY, JSON.stringify(items)); } catch { /* تخزين محجوب: القائمة لهذه الجلسة */ }
}

export function WishlistProvider({ children }) {
  const { user, loading: sessionLoading, canManageStore } = useAuth();
  const toast = useToast();
  const [items, setItems] = useState(readLocal);
  const [onServer, setOnServer] = useState(false);

  // عند كل تبدّل للمستخدم (استعادة الجلسة، دخول، خروج): العميل ⇒ دمج المحلي (إن وُجد) ثم قائمة الخادم؛ غيره ⇒ المحلي. الفشل
  // (شبكة، وحدة معطّلة) يُبقي المحلي دون فقده — الدمج يُعاد في الدخول التالي.
  const userId = user?.id;
  useEffect(() => {
    if (sessionLoading) return undefined;
    if (!userId || canManageStore) {
      setOnServer(false);
      setItems(readLocal());
      return undefined;
    }

    let active = true;
    const pending = localIds(readLocal());
    (pending.length ? api.mergeWishlist(pending) : api.getWishlist())
      .then((wishlist) => {
        if (!active) return;
        if (pending.length) writeLocal([]);
        setOnServer(true);
        setItems(wishlist.items);
      })
      .catch((e) => {
        if (!active) return;
        setOnServer(false);
        setItems(readLocal());
        if (!serverUnavailable(e)) console.warn('wishlist sync failed', e.code ?? e.message);
      });
    return () => { active = false; };
  }, [sessionLoading, userId, canManageStore]);

  // القائمة المحلية تُحفظ بعد كل تغيير — لا حين تكون القائمة من الخادم (العميل لا يترك أثراً على جهاز مشترك).
  useEffect(() => {
    if (!sessionLoading && !onServer) writeLocal(items);
  }, [items, onServer, sessionLoading]);

  const has = useCallback((id) => items.some((i) => i.id === id), [items]);

  // على الخادم: كل عملية تعيد المفضّلة كاملة فتُستبدل الحالة بها. الخطأ يُعرض ثم تُعاد قراءة القائمة كي لا تبقى الواجهة مخالفة.
  const sync = useCallback(async (operation) => {
    try {
      setItems((await operation()).items);
    } catch (e) {
      toast.error(e.message);
      api.getWishlist().then((w) => setItems(w.items)).catch(() => {});
    }
  }, [toast]);

  const toggle = useCallback((product) => {
    if (!onServer) {
      setItems((list) => toggleLocal(list, product));
      return Promise.resolve();
    }
    return sync(() => (has(product.id) ? api.removeFromWishlist(product.id) : api.addToWishlist(product.id)));
  }, [onServer, has, sync]);

  const remove = useCallback((id) => {
    if (!onServer) {
      setItems((list) => list.filter((i) => i.id !== id));
      return Promise.resolve();
    }
    return sync(() => api.removeFromWishlist(id));
  }, [onServer, sync]);

  const value = useMemo(
    () => ({ items, count: items.length, has, toggle, remove, onServer }),
    [items, has, toggle, remove, onServer]);
  return <WishlistContext.Provider value={value}>{children}</WishlistContext.Provider>;
}

export const useWishlist = () => useContext(WishlistContext);
