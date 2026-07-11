import { createContext, useContext, useState, useEffect, useCallback } from 'react';

// ============================================================================
// WishlistContext — قائمة مفضّلة محلية بالكامل (بلا حساب مطلوب، بلا خادم).
// نخزّن كائن المنتج كاملاً لا معرّفه فقط، كي تُعرض صفحة /wishlist فوراً من
// localStorage دون إعادة جلب كل منتج من الـ API عند كل زيارة.
// ============================================================================
const WishlistContext = createContext();
const STORAGE_KEY = 'souq_wishlist';

function loadStored() {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    const parsed = raw ? JSON.parse(raw) : [];
    return Array.isArray(parsed) ? parsed : [];
  } catch { return []; }
}

export function WishlistProvider({ children }) {
  const [items, setItems] = useState(loadStored);

  useEffect(() => {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(items));
  }, [items]);

  const has = useCallback((id) => items.some((i) => i.id === id), [items]);

  // تبديل: يُضيف المنتج إن غاب، يُزيله إن كان موجوداً — زر قلب واحد لكل الحالتين.
  const toggle = useCallback((product) => {
    setItems((list) =>
      list.some((i) => i.id === product.id)
        ? list.filter((i) => i.id !== product.id)
        : [...list, product]);
  }, []);

  const remove = useCallback((id) => {
    setItems((list) => list.filter((i) => i.id !== id));
  }, []);

  const value = { items, count: items.length, has, toggle, remove };
  return <WishlistContext.Provider value={value}>{children}</WishlistContext.Provider>;
}

export const useWishlist = () => useContext(WishlistContext);
