import i18n from '../i18n';

// ============================================================================
// عميل API موحّد — لماذا ملف واحد؟ (مبدأ DRY + فصل الاهتمامات)
// بدل تكرار إعدادات fetch (الرابط، الترويسات، معالجة الأخطاء) في كل مكوّن،
// نجمعها هنا. لو تغيّر عنوان الـ API أو طريقة المصادقة، نُعدّل هذا الملف فقط.
// ============================================================================
const BASE = '/api';
const TOKEN_KEY = 'souq_token';

// تخزين التوكن:
// نستخدم localStorage في التطوير لبساطته. للإنتاج، الأصحّ هو httpOnly cookie
// لأن localStorage يمكن قراءته بأي سكربت يعمل في الصفحة — فثغرة XSS واحدة تكشف
// التوكن. الـ httpOnly cookie لا يقرؤه JavaScript إطلاقاً. سنحوّل إليه في مرحلة
// التقوية/النشر (المراحل 4 و7) مع ضبط SameSite/Secure على الخادم.
export const tokenStore = {
  get: () => localStorage.getItem(TOKEN_KEY),
  set: (t) => localStorage.setItem(TOKEN_KEY, t),
  clear: () => localStorage.removeItem(TOKEN_KEY),
};

async function request(path, options = {}) {
  const token = tokenStore.get();
  const res = await fetch(BASE + path, {
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...options.headers,
    },
    ...options,
  });

  // توكن منتهٍ/غير صالح: ننظّف الجلسة ونُعلم التطبيق ليعيد التوجيه للدخول.
  if (res.status === 401) {
    tokenStore.clear();
    window.dispatchEvent(new Event('auth:unauthorized'));
  }

  if (!res.ok) {
    const body = await res.json().catch(() => ({}));
    const err = new Error(body.error || i18n.t('errors.connection'));
    err.status = res.status;
    err.code = body.code;
    throw err;
  }
  return res.status === 204 ? null : res.json();
}

// رفع ملف (multipart/form-data): لا نضبط Content-Type يدوياً — المتصفح يولّد
// حدّ الأجزاء (boundary) بنفسه، وضبطه يدوياً يكسر الطلب.
async function upload(path, formData) {
  const token = tokenStore.get();
  const res = await fetch(BASE + path, {
    method: 'POST',
    headers: token ? { Authorization: `Bearer ${token}` } : {},
    body: formData,
  });

  if (res.status === 401) {
    tokenStore.clear();
    window.dispatchEvent(new Event('auth:unauthorized'));
  }

  if (!res.ok) {
    const body = await res.json().catch(() => ({}));
    const err = new Error(body.error || i18n.t('errors.upload'));
    err.status = res.status;
    err.code = body.code;
    throw err;
  }
  return res.json();
}

// دوال معبّرة بأسماء المجال، تخفي تفاصيل HTTP عن بقية التطبيق.
export const api = {
  // ── المصادقة ──
  register: (payload) => request('/auth/register', { method: 'POST', body: JSON.stringify(payload) }),
  login: (payload) => request('/auth/login', { method: 'POST', body: JSON.stringify(payload) }),
  forgotPassword: (email) => request('/auth/forgot-password', { method: 'POST', body: JSON.stringify({ email }) }),
  resetPassword: (token, newPassword) =>
    request('/auth/reset-password', { method: 'POST', body: JSON.stringify({ token, newPassword }) }),

  // ── الكتالوج ──
  getProducts: (params = {}) => {
    const q = new URLSearchParams(
      Object.entries(params).filter(([, v]) => v != null && v !== '')
    ).toString();
    return request(`/products?${q}`);
  },
  getProduct: (id) => request(`/products/${id}`),
  getCategories: () => request('/categories'),

  // ── الطلبات ──
  createOrder: (payload) => request('/orders', { method: 'POST', body: JSON.stringify(payload) }),
  getOrder: (id) => request(`/orders/${id}`),
  confirmOrderPayment: (id) => request(`/orders/${id}/confirm-payment`, { method: 'POST' }),

  // ── الدفع (Stripe) ──
  getPaymentConfig: () => request('/payments/config'),

  // ── الكوبونات ──
  applyCoupon: (code, subtotal, currency = 'JOD') =>
    request(`/coupons/apply?${new URLSearchParams({ code, subtotal, currency })}`),
  getCoupons: (params = {}) => {
    const q = new URLSearchParams(
      Object.entries(params).filter(([, v]) => v != null && v !== '')
    ).toString();
    return request(`/coupons?${q}`);
  },
  createCoupon: (payload) => request('/coupons', { method: 'POST', body: JSON.stringify(payload) }),
  updateCoupon: (id, payload) => request(`/coupons/${id}`, { method: 'PUT', body: JSON.stringify(payload) }),
  deleteCoupon: (id) => request(`/coupons/${id}`, { method: 'DELETE' }),

  // ── التقييمات ──
  getProductReviews: (productId, params = {}) => {
    const q = new URLSearchParams(
      Object.entries(params).filter(([, v]) => v != null && v !== '')
    ).toString();
    return request(`/products/${productId}/reviews?${q}`);
  },
  createReview: (productId, payload) =>
    request(`/products/${productId}/reviews`, { method: 'POST', body: JSON.stringify(payload) }),

  // ── إدارة المنتجات (أدمن) ──
  createProduct: (payload) => request('/products', { method: 'POST', body: JSON.stringify(payload) }),
  updateProduct: (id, payload) => request(`/products/${id}`, { method: 'PUT', body: JSON.stringify(payload) }),
  deleteProduct: (id) => request(`/products/${id}`, { method: 'DELETE' }),
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

  // ── إدارة الفئات (أدمن) ──
  createCategory: (payload) => request('/categories', { method: 'POST', body: JSON.stringify(payload) }),
  updateCategory: (id, payload) => request(`/categories/${id}`, { method: 'PUT', body: JSON.stringify(payload) }),
  deleteCategory: (id) => request(`/categories/${id}`, { method: 'DELETE' }),

  // ── إدارة الطلبات (أدمن) ──
  getOrders: (params = {}) => {
    const q = new URLSearchParams(
      Object.entries(params).filter(([, v]) => v != null && v !== '')
    ).toString();
    return request(`/orders?${q}`);
  },
  updateOrderStatus: (id, action) =>
    request(`/orders/${id}/status`, { method: 'PUT', body: JSON.stringify({ action }) }),
};
