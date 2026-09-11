// ============================================================================
// المفضّلة (المرحلة 13، ADR-0033): الزائر يحفظ قائمته في متصفّحه، والعميل على الخادم — تتبعه بين أجهزته بأسعار الكتالوج
// الحيّة، والقائمة المحلية تُدمج في حسابه عند دخوله ثم تُفرَّغ. منطق خالص مُختبَر بـ Vitest؛ الشبكة والتخزين في WishlistContext.
// ============================================================================
export const WISHLIST_STORAGE_KEY = 'souq_wishlist';

// سقف المفضّلة على الخادم (WishlistItem.MaxItemsPerCustomer) — الدمج الواحد لا يتجاوزه.
export const MAX_WISHLIST = 200;

// المعرّفات للدمج: أعداد صحيحة موجبة بلا تكرار، بترتيب الحفظ (الأقدم أولاً)، والأحدث MAX_WISHLIST فقط. القائمة المحلية قد
// تحمل عناصر بشكل قديم أو تالفة — تُتجاهل ولا تُفشل الدخول.
export function localIds(items) {
  const ids = [];
  for (const item of Array.isArray(items) ? items : []) {
    const id = Number(item?.id);
    if (Number.isInteger(id) && id > 0 && !ids.includes(id)) ids.push(id);
  }
  return ids.slice(-MAX_WISHLIST);
}

// تبديل منتج في القائمة المحلية: يُضاف إن غاب، ويُزال إن وُجد — زرّ قلب واحد للحالتين.
export function toggleLocal(items, product) {
  return items.some((i) => i.id === product.id)
    ? items.filter((i) => i.id !== product.id)
    : [...items, product];
}

// لا مفضّلة على الخادم لهذا الحساب: الوحدة معطّلة في المتجر، أو حساب بلا ملف عميل (403) ⇒ القائمة المحلية وحدها.
export function serverUnavailable(error) {
  return error?.code === 'ModuleDisabled' || error?.status === 403;
}

export function parseStored(raw) {
  try {
    const parsed = raw ? JSON.parse(raw) : [];
    return Array.isArray(parsed) ? parsed : [];
  } catch { return []; }
}
