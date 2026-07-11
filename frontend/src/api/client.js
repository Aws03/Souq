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
    const err = new Error(body.error || 'حدث خطأ في الاتصال بالخادم');
    err.status = res.status;
    err.code = body.code;
    throw err;
  }
  return res.status === 204 ? null : res.json();
}

// دوال معبّرة بأسماء المجال، تخفي تفاصيل HTTP عن بقية التطبيق.
export const api = {
  // ── المصادقة ──
  register: (payload) => request('/auth/register', { method: 'POST', body: JSON.stringify(payload) }),
  login: (payload) => request('/auth/login', { method: 'POST', body: JSON.stringify(payload) }),

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
};
