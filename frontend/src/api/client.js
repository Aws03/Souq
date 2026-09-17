import i18n from '../i18n';
import { toApiError } from './problem';
import { toQueryString } from './query';

// ============================================================================
// عميل API موحّد — لماذا ملف واحد؟ (مبدأ DRY + فصل الاهتمامات)
// بدل تكرار إعدادات fetch (الرابط، الترويسات، معالجة الأخطاء) في كل مكوّن،
// نجمعها هنا. لو تغيّر عنوان الـ API أو طريقة المصادقة، نُعدّل هذا الملف فقط.
// ============================================================================
const BASE = '/api';

// ============================================================================
// الجلسة (ADR-0010): توكن الوصول (15 دقيقة) في ذاكرة هذا الملف فقط — لا localStorage، فسكربت محقون
// لا يجد توكناً مخزّناً يسرقه، ولا يغادر التوكن هذا الملف أصلاً. رمز التجديد في ملف تعريف ارتباط
// HttpOnly مقصور على /api/auth لا يقرؤه JavaScript. عند 401 لطلب حمل توكناً: تجديد واحد مشترك ثم
// إعادة المحاولة مرّة؛ رفض التجديد ⇒ حدث SESSION_EXPIRED يعيد التطبيق لحالة الزائر.
// ============================================================================
export const SESSION_EXPIRED = 'session-expired';
export const authEvents = new EventTarget();

let accessToken = null;
let refreshing = null;

// يحفظ توكن الاستجابة ويعيد المستخدم وحده للمتصل.
function startSession(auth) {
  accessToken = auth?.accessToken ?? null;
  return auth?.user ?? null;
}

// ============================================================================
// تجديد صامت: المستخدم إن بقيت جلسة، وإلا null. طلب واحد مهما تزامن المتصلون — كل تجديد يدوّر الرمز،
// وتجديدان متوازيان بالرمز نفسه يبدوان للخادم إعادة استخدام (سرقة) خارج مهلة السباق.
//
// "فشل التجديد" ليس شيئاً واحداً، وخلطُ الاثنين كان عيباً حقيقياً ظهر في تحقّق المتصفّح
// (المرحلة 16 §22): 401 تعني أن الجلسة انتهت فعلاً، بينما 429 أو 503 أو انقطاع شبكة تعني
// أننا *لا نعرف*. الشيفرة كانت تعامل كل ما ليس ok كانتهاء، فحدُّ معدّل عابر على /auth/refresh
// كان يُخرج الزبون من جلسته ويرميه إلى صفحة الدخول — وهو أمر يُصيب زبوناً خلف عنوان مشترك
// (مكتب، مشغّل جوّال) بلا أي خطأ منه، وقد يقع في منتصف الدفع.
//
// النتيجة الآن ثلاثية: مستخدم، أو انتهاء مؤكّد، أو "غير معروف" — والأخيرة لا تُسقط جلسة.
// ============================================================================
export const REFRESH_EXPIRED = 'expired';       // الخادم قال: لا جلسة
export const REFRESH_UNKNOWN = 'unknown';       // حدّ معدّل، عطل خادم، أو شبكة — لا حكم

export function refreshSession() {
  if (!refreshing) {
    refreshing = fetch(`${BASE}/auth/refresh`, { method: 'POST', credentials: 'same-origin' })
      .then(async (res) => {
        if (res.ok) return { user: startSession(await res.json()), outcome: 'active' };
        // 401/403 فقط جواب نهائي؛ ما عداها ظرف لا حكم.
        const expired = res.status === 401 || res.status === 403;
        // انتهاء مؤكّد ⇒ يُسقَط التوكن القديم فلا يُرسل بعدها. "غير معروف" ⇒ يبقى كما هو،
        // فالمحاولة التالية قد تنجح ولا داعي لهدم جلسة قد تكون حيّة.
        if (expired) startSession(null);
        return { user: null, outcome: expired ? REFRESH_EXPIRED : REFRESH_UNKNOWN };
      })
      .catch(() => ({ user: null, outcome: REFRESH_UNKNOWN }))   // شبكة مقطوعة ليست خروجاً
      .finally(() => { refreshing = null; });
  }
  return refreshing;
}

// أي استجابة غير ناجحة ⇒ Error موحّد من ProblemDetails (انظر problem.js). رسائل الخادم
// عربية، فالواجهة العربية تعرضها كما هي؛ غيرها يترجم الرمز الثابت (errors.codes.*).
async function readApiError(res, fallbackKey) {
  const body = await res.json().catch(() => null);
  return toApiError(res.status, body, {
    translate: (code) => (i18n.exists(`errors.codes.${code}`) ? i18n.t(`errors.codes.${code}`) : null),
    preferServerDetail: (i18n.language || 'ar').startsWith('ar'),
    fallbackMessage: i18n.t(fallbackKey),
  });
}

async function send(path, init, { fallbackKey = 'errors.connection', anonymous = false } = {}) {
  const attempt = () => fetch(BASE + path, {
    ...init,
    credentials: 'same-origin',
    headers: { ...init.headers, ...(accessToken && !anonymous ? { Authorization: `Bearer ${accessToken}` } : {}) },
  });

  const sentToken = accessToken !== null && !anonymous;
  let res = await attempt();
  if (res.status === 401 && sentToken) {
    const { user, outcome } = await refreshSession();
    if (user) res = await attempt();
    // الحدث يُطلق على الانتهاء المؤكّد وحده: "لا نعرف" تترك الجلسة كما هي ويُعاد المحاولة لاحقاً.
    else if (outcome === REFRESH_EXPIRED) authEvents.dispatchEvent(new Event(SESSION_EXPIRED));
  }

  if (!res.ok) throw await readApiError(res, fallbackKey);
  return res.status === 204 ? null : res.json();
}

function request(path, options = {}) {
  return send(path, { ...options, headers: { 'Content-Type': 'application/json', ...options.headers } });
}

// رفع ملف (multipart/form-data): لا نضبط Content-Type يدوياً — المتصفح يولّد
// حدّ الأجزاء (boundary) بنفسه، وضبطه يدوياً يكسر الطلب.
function upload(path, formData) {
  return send(path, { method: 'POST', body: formData }, { fallbackKey: 'errors.upload' });
}

// نقاط المصادقة العامة: بلا توكن ولا تجديد — 401 هنا (كلمة مرور خاطئة) جواب، لا انتهاء جلسة.
function publicAuth(path, payload) {
  return send(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: payload === undefined ? undefined : JSON.stringify(payload),
  }, { anonymous: true });
}

// دوال معبّرة بأسماء المجال، تخفي تفاصيل HTTP عن بقية التطبيق.
export const api = {
  // ── المصادقة ── (الدخول/التسجيل/تغيير الكلمة تبدأ جلسة: التوكن يبقى هنا والمستخدم يعود للمتصل)
  register: async (payload) => startSession(await publicAuth('/auth/register', payload)),
  login: async (payload) => startSession(await publicAuth('/auth/login', payload)),
  logout: async () => { accessToken = null; await publicAuth('/auth/logout'); },
  me: () => request('/auth/me'),
  changePassword: async (currentPassword, newPassword) => startSession(await request('/auth/change-password',
    { method: 'POST', body: JSON.stringify({ currentPassword, newPassword }) })),
  forgotPassword: (email) => publicAuth('/auth/forgot-password', { email }),
  resetPassword: (token, newPassword) => publicAuth('/auth/reset-password', { token, newPassword }),
  verifyEmail: (token) => publicAuth('/auth/verify-email', { token }),
  resendVerification: () => request('/auth/resend-verification', { method: 'POST' }),

  // ── حساب العميل (المرحلة 7) ── العميل هو المستخدم الحالي دائماً؛ لا معرّف عميل في أي طلب.
  getMyProfile: () => request('/account/profile'),
  updateMyProfile: (payload) => request('/account/profile', { method: 'PUT', body: JSON.stringify(payload) }),
  getMyAddresses: () => request('/account/addresses'),
  addMyAddress: (address, { defaultShipping = false, defaultBilling = false } = {}) =>
    request('/account/addresses', { method: 'POST', body: JSON.stringify({ address, defaultShipping, defaultBilling }) }),
  updateMyAddress: (id, address) => request(`/account/addresses/${id}`, { method: 'PUT', body: JSON.stringify(address) }),
  removeMyAddress: (id) => request(`/account/addresses/${id}`, { method: 'DELETE' }),
  setMyDefaultAddress: (id, use) => request(`/account/addresses/${id}/default-${use}`, { method: 'PUT' }),
  exportMyData: () => request('/account/export'),
  eraseMyAccount: (password) => request('/account/erase', { method: 'POST', body: JSON.stringify({ password }) }),

  // ── إعداد متجر المضيف (المرحلة 15) ── أول طلب عند الإقلاع: الهوية واللغات والعملة والوحدات. المتجر من المضيف لا من الطلب.
  getStorefrontConfig: () => request('/storefront/config'),

  // ── الكتالوج ── (سلسلة الاستعلام لكل القوائم من toQueryString: مصفوفات بمفتاح متكرّر)
  getProducts: (params = {}) => request(`/products${toQueryString(params)}`),
  getProduct: (id) => request(`/products/${id}`),
  getProductBySlug: (slug) => request(`/products/by-slug/${encodeURIComponent(slug)}`),
  getRelatedProducts: (id, count = 6) => request(`/products/${id}/related?count=${count}`),
  getCategories: () => request('/categories'),

  // ── الطلبات ──
  createOrder: (payload) => request('/orders', { method: 'POST', body: JSON.stringify(payload) }),
  getOrder: (id) => request(`/orders/${id}`),
  confirmOrderPayment: (id) => request(`/orders/${id}/confirm-payment`, { method: 'POST' }),
  // مرقّمة (PaginatedList): { items, totalCount, totalPages, ... }
  getMyOrders: (params = {}) => request(`/orders/mine${toQueryString(params)}`),

  // ── السلة (المرحلة 8) ── الزائر يُعرَّف بملف تعريف ارتباط HttpOnly يضعه الخادم (لا يراه هذا الملف)، والعميل بجلسته.
  // كل عملية تعيد السلة كاملة مسعَّرةً بالخطّ نفسه الذي يُنشئ الطلب.
  getBasket: () => request('/basket'),
  // shipping (المرحلة 12): { methodId, country } — طريقة الشحن المختارة ودولة العنوان.
  quoteBasket: (couponCode, shipping = {}) =>
    request(`/basket/quote${toQueryString({ couponCode, shippingMethodId: shipping.methodId, country: shipping.country })}`),
  addToBasket: (productId, quantity = 1) =>
    request('/basket/items', { method: 'POST', body: JSON.stringify({ productId, quantity }) }),
  setBasketQuantity: (productId, quantity) =>
    request(`/basket/items/${productId}`, { method: 'PUT', body: JSON.stringify({ quantity }) }),
  removeFromBasket: (productId) => request(`/basket/items/${productId}`, { method: 'DELETE' }),
  // تتبّع بلا مصادقة (رابط قابل للمشاركة) — نفس نقطة الخادم العامة تُستخدم هنا
  // وفي صفحة تفصيل الطلب داخل التطبيق معاً (لا فرق بين الحالتين من الواجهة).
  // رابط التتبّع العام بالرمز العشوائي (المرحلة 9 — لا بالمعرّف التسلسلي، B8).
  trackOrder: (token) => request(`/orders/track/${encodeURIComponent(token)}`),
  // إلغاء العميل طلبه قبل الدفع (المرحلة 9).
  cancelMyOrder: (id, reason = null) => request(`/orders/${id}/cancel`, { method: 'POST', body: JSON.stringify({ reason }) }),

  // ── الدفع (Stripe) ──
  getPaymentConfig: () => request('/payments/config'),

  // ── الكوبونات (إدارة) ── معاينة الخصم للعميل من /basket/quote (المرحلة 8): الخطّ نفسه الذي يُنشئ الطلب.
  getCoupons: (params = {}) => request(`/coupons${toQueryString(params)}`),
  // استخدامات كوبون (المرحلة 10): أيّ طلب ولأيّ عميل وحالته.
  getCouponRedemptions: (id, params = {}) => request(`/coupons/${id}/redemptions${toQueryString(params)}`),
  createCoupon: (payload) => request('/coupons', { method: 'POST', body: JSON.stringify(payload) }),
  updateCoupon: (id, payload) => request(`/coupons/${id}`, { method: 'PUT', body: JSON.stringify(payload) }),
  deleteCoupon: (id) => request(`/coupons/${id}`, { method: 'DELETE' }),

  // ── التقييمات ── العامة: المعتمد وحده مع المتوسط والتوزيع؛ الإنشاء يعيد { id, status } (Pending ⇒ بانتظار مراجعة المتجر).
  getProductReviews: (productId, params = {}) => request(`/products/${productId}/reviews${toQueryString(params)}`),
  createReview: (productId, payload) =>
    request(`/products/${productId}/reviews`, { method: 'POST', body: JSON.stringify(payload) }),
  // الإشراف (المرحلة 13، reviews.moderate): الطابور بالحالة، والاعتماد، والرفض بملاحظة للإدارة؛ سياسة النشر إعداد متجر.
  getAdminReviews: (params = {}) => request(`/admin/reviews${toQueryString(params)}`),
  approveReview: (id) => request(`/admin/reviews/${id}/approve`, { method: 'POST' }),
  rejectReview: (id, note = null) =>
    request(`/admin/reviews/${id}/reject`, { method: 'POST', body: JSON.stringify({ note }) }),
  getReviewSettings: () => request('/admin/reviews/settings'),
  updateReviewSettings: (autoApprove) =>
    request('/admin/reviews/settings', { method: 'PUT', body: JSON.stringify({ autoApprove }) }),

  // ── المفضّلة (المرحلة 13) ── للعميل المسجّل، وكل عملية تعيد المفضّلة كاملة. الزائر يحفظها في متصفّحه (WishlistContext).
  getWishlist: () => request('/wishlist'),
  addToWishlist: (productId) => request(`/wishlist/${productId}`, { method: 'PUT' }),
  removeFromWishlist: (productId) => request(`/wishlist/${productId}`, { method: 'DELETE' }),
  mergeWishlist: (productIds) => request('/wishlist/merge', { method: 'POST', body: JSON.stringify({ productIds }) }),

  // ── الإشعارات (المرحلة 14) ── للحساب الحالي: الشارة تسأل العدد دورياً، والقائمة تُجلب عند فتحها.
  getNotifications: (params = {}) => request(`/notifications${toQueryString(params)}`),
  getUnreadNotificationCount: () => request('/notifications/unread-count'),
  markNotificationRead: (id) => request(`/notifications/${id}/read`, { method: 'POST' }),
  markAllNotificationsRead: () => request('/notifications/read-all', { method: 'POST' }),

  // ── إحصاءات المنصّة (مضيف المنصّة، platform.reports.view) ── أعداد عبر كل المتاجر،
  // مجمَّعة لا صفوفاً: لا بيانات متجر بعينه تعبر إلى هنا.
  getPlatformStats: () => request('/platform/stats'),

  // ── متاجر المنصّة (مضيف المنصّة، platform.tenants.manage) ── هنا وحدها يأتي معرّف متجر في المسار، وكل طلب
  // مُدقَّق على الخادم. التجهيز خطوات مستقلّة تُحفظ كلٌّ منها فوراً: متجر لم يكتمل تجهيزه يبقى "قيد التجهيز"
  // مغلقاً للزوّار، ويُستأنف من حيث توقّف — لا معاملة تمتدّ عبر شاشات.
  getProvisioningOptions: () => request('/platform/tenants/options'),
  getPlatformStores: (params = {}) => request(`/platform/tenants${toQueryString(params)}`),
  getPlatformStore: (id) => request(`/platform/tenants/${id}`),
  createPlatformStore: (payload) => request('/platform/tenants', { method: 'POST', body: JSON.stringify(payload) }),
  updatePlatformStore: (id, payload) => request(`/platform/tenants/${id}`, { method: 'PUT', body: JSON.stringify(payload) }),
  // action: Activate | Suspend | Archive
  changePlatformStoreStatus: (id, action) =>
    request(`/platform/tenants/${id}/status`, { method: 'POST', body: JSON.stringify({ action }) }),
  addPlatformStoreDomain: (id, host) =>
    request(`/platform/tenants/${id}/domains`, { method: 'POST', body: JSON.stringify({ host }) }),
  removePlatformStoreDomain: (id, host) =>
    request(`/platform/tenants/${id}/domains/${encodeURIComponent(host)}`, { method: 'DELETE' }),
  setPlatformStorePrimaryDomain: (id, host) =>
    request(`/platform/tenants/${id}/domains/${encodeURIComponent(host)}/primary`, { method: 'POST' }),
  verifyPlatformStoreDomain: (id, host) =>
    request(`/platform/tenants/${id}/domains/${encodeURIComponent(host)}/verify`, { method: 'POST' }),
  updatePlatformStoreSettings: (id, payload) =>
    request(`/platform/tenants/${id}/settings`, { method: 'PUT', body: JSON.stringify(payload) }),
  setPlatformStoreModules: (id, modules) =>
    request(`/platform/tenants/${id}/modules`, { method: 'PUT', body: JSON.stringify({ modules }) }),
  // المحرّر يسمّي الملف logo | favicon | social-image؛ نقطة المنصّة تأخذ اسم التعداد (BrandingAsset).
  uploadPlatformStoreBranding: (id, asset, file) => {
    const form = new FormData();
    form.append('file', file);
    const name = { logo: 'Logo', favicon: 'Favicon', 'social-image': 'SocialImage' }[asset];
    return upload(`/platform/tenants/${id}/branding/${name}`, form);
  },
  getPlatformStoreAccounts: (id, params = {}) => request(`/platform/tenants/${id}/accounts${toQueryString(params)}`),
  invitePlatformStoreAdmin: (id, payload) =>
    request(`/platform/tenants/${id}/admins`, { method: 'POST', body: JSON.stringify(payload) }),

  // ── حسابات المنصّة (مضيف المنصّة، platform.users.manage — المالك وحده) ── دعوة بالبريد، وتفعيل/إيقاف يُسقط
  // جلسة الحساب فوراً على الخادم. لا تعديل دور ولا حذف: الخادم لا يقدّمهما.
  getPlatformUsers: (params = {}) => request(`/platform/users${toQueryString(params)}`),
  invitePlatformUser: (payload) => request('/platform/users', { method: 'POST', body: JSON.stringify(payload) }),
  setPlatformUserStatus: (id, active) =>
    request(`/platform/users/${id}/status`, { method: 'POST', body: JSON.stringify({ active }) }),

  // ── سجلّ التدقيق (مضيف المنصّة، platform.audit.view) ── ترقيم وتصفية من الخادم: متجر، بادئة فعل، حساب، مدّة.
  getPlatformAudit: (params = {}) => request(`/platform/audit${toQueryString(params)}`),

  // ── تقارير المتجر (أدمن، store.reports.view) ── استجابة واحدة للوحة كاملة: بطاقاتها من
  // لحظة واحدة لا من اثنتي عشرة، والمدّة مفتاح مغلق لا تاريخان من المتصفّح.
  getStoreDashboard: (range = 'Last30Days') => request(`/admin/reports/dashboard?range=${range}`),

  // ── إدارة المنتجات (أدمن) ── القائمة والتفاصيل من /admin (كل الحالات وكل اللغات والصور بمعرّفاتها)
  getAdminProducts: (params = {}) => request(`/admin/products${toQueryString(params)}`),
  getAdminProduct: (id) => request(`/admin/products/${id}`),
  setProductStatus: (id, status) =>
    request(`/admin/products/${id}/status`, { method: 'PUT', body: JSON.stringify({ status }) }),
  removeProductImage: (id, imageId) => request(`/admin/products/${id}/images/${imageId}`, { method: 'DELETE' }),
  reorderProductImages: (id, imageIds) =>
    request(`/admin/products/${id}/images/order`, { method: 'PUT', body: JSON.stringify({ imageIds }) }),
  createProduct: (payload) => request('/products', { method: 'POST', body: JSON.stringify(payload) }),
  updateProduct: (id, payload) => request(`/products/${id}`, { method: 'PUT', body: JSON.stringify(payload) }),
  uploadProductImage: (id, file) => {
    const form = new FormData();
    form.append('file', file);
    return upload(`/products/${id}/image`, form);
  },
  uploadProductVideo: (id, file) => {
    const form = new FormData();
    form.append('file', file);
    return upload(`/products/${id}/video`, form);
  },

  // ── إدارة الفئات (أدمن) ── القائمة من /admin تشمل المعطّلة
  getAdminCategories: () => request('/admin/categories'),
  createCategory: (payload) => request('/categories', { method: 'POST', body: JSON.stringify(payload) }),
  updateCategory: (id, payload) => request(`/categories/${id}`, { method: 'PUT', body: JSON.stringify(payload) }),
  deleteCategory: (id) => request(`/categories/${id}`, { method: 'DELETE' }),

  // ── جرد المخزون (أدمن) ──
  // كلها مرقّمة (PaginatedList). الشارة تكفيها low-stock بـ pageSize=1 ثم totalCount.
  getInventory: (params = {}) => request(`/admin/inventory${toQueryString(params)}`),
  getLowStock: (params = {}) => request(`/admin/inventory/low-stock${toQueryString(params)}`),
  getStockMovements: (productId, params = {}) =>
    request(`/admin/inventory/${productId}/movements${toQueryString(params)}`),
  // تصحيح بفارق وسبب (inventory.manage) يعيد مستوى المخزون الجديد — لا تعيين مطلق (C4).
  adjustStock: (productId, delta, reason) =>
    request(`/admin/inventory/${productId}/adjustments`, { method: 'POST', body: JSON.stringify({ delta, reason }) }),
  setStockThreshold: (productId, lowStockThreshold) =>
    request(`/admin/inventory/${productId}/threshold`, { method: 'PUT', body: JSON.stringify({ lowStockThreshold }) }),

  // ── عملاء المتجر (أدمن، المرحلة 7) ── سجلّ طلبات عميل من getOrders({ customerId }).
  getCustomers: (params = {}) => request(`/admin/customers${toQueryString(params)}`),
  getCustomer: (id) => request(`/admin/customers/${id}`),
  setCustomerStatus: (id, status) =>
    request(`/admin/customers/${id}/status`, { method: 'PUT', body: JSON.stringify({ status }) }),
  exportCustomer: (id) => request(`/admin/customers/${id}/export`),
  eraseCustomer: (id) => request(`/admin/customers/${id}/erase`, { method: 'POST' }),

  // ── إدارة الطلبات (أدمن) ──
  getOrders: (params = {}) => request(`/orders${toQueryString(params)}`),
  // استرداد دفعة طلب (المرحلة 11): بلا amount ⇒ كل المتبقّي؛ retry لاسترداد معلّق (المفتاح نفسه لدى البوّابة).
  refundOrder: (id, payload = {}) => request(`/orders/${id}/refunds`, { method: 'POST', body: JSON.stringify(payload) }),
  retryRefund: (id, refundId) => request(`/orders/${id}/refunds/${refundId}/retry`, { method: 'POST' }),
  // ── إعدادات المتجر (store.settings.manage) ── المتجر هو متجر المضيف دائماً: لا معرّف في أيّ مسار.
  // options: القوائم والحدود التي يقبلها الخادم — الواجهة لا تحمل نسخة منها.
  getStoreSettings: () => request('/admin/store/settings'),
  getStoreSettingsOptions: () => request('/admin/store/settings/options'),
  updateStoreSettings: (payload) => request('/admin/store/settings', { method: 'PUT', body: JSON.stringify(payload) }),
  // asset: logo | favicon | social-image. الملف يُفحص بمحتواه على الخادم، والرابط يولّده الخادم.
  uploadStoreBranding: (asset, file) => {
    const form = new FormData();
    form.append('file', file);
    return upload(`/admin/store/branding/${asset}`, form);
  },
  // ── فريق المتجر (store.staff.manage) ── دعوة البريد نفسه وهو معلّق تُجدّد الدعوة (renewed).
  getStaff: (params = {}) => request(`/admin/staff${toQueryString(params)}`),
  inviteStaff: (payload) => request('/admin/staff', { method: 'POST', body: JSON.stringify(payload) }),
  setStaffStatus: (id, active) =>
    request(`/admin/staff/${id}/status`, { method: 'POST', body: JSON.stringify({ active }) }),
  // حساب بوّابة الدفع الخاص بالمتجر (المرحلة 11): السرّان يُكتبان ولا يُقرآن.
  getStorePayments: () => request('/admin/store/payments'),
  updateStorePayments: (payload) => request('/admin/store/payments', { method: 'PUT', body: JSON.stringify(payload) }),
  removeStorePayments: () => request('/admin/store/payments', { method: 'DELETE' }),
  // طرق الشحن (إدارة، المرحلة 12).
  getShippingMethods: () => request('/admin/shipping-methods'),
  createShippingMethod: (payload) => request('/admin/shipping-methods', { method: 'POST', body: JSON.stringify(payload) }),
  updateShippingMethod: (id, payload) =>
    request(`/admin/shipping-methods/${id}`, { method: 'PUT', body: JSON.stringify(payload) }),
  deleteShippingMethod: (id) => request(`/admin/shipping-methods/${id}`, { method: 'DELETE' }),
  updateOrderStatus: (id, action, extra = {}) =>
    request(`/orders/${id}/status`, { method: 'PUT', body: JSON.stringify({ action, ...extra }) }),
};
