// ============================================================================
// عميل API موحّد — لماذا ملف واحد؟ (مبدأ DRY + فصل الاهتمامات)
// بدل تكرار إعدادات fetch (الرابط، الترويسات، معالجة الأخطاء) في كل مكوّن،
// نجمعها هنا. لو تغيّر عنوان الـ API أو طريقة المصادقة، نُعدّل هذا الملف فقط.
// ============================================================================
const BASE = '/api';

async function request(path, options = {}) {
  const res = await fetch(BASE + path, {
    headers: { 'Content-Type': 'application/json' },
    ...options,
  });
  if (!res.ok) {
    const body = await res.json().catch(() => ({}));
    throw new Error(body.error || 'حدث خطأ في الاتصال بالخادم');
  }
  return res.status === 204 ? null : res.json();
}

// دوال معبّرة بأسماء المجال، تخفي تفاصيل HTTP عن بقية التطبيق.
export const api = {
  getProducts: (params = {}) => {
    const q = new URLSearchParams(
      Object.entries(params).filter(([, v]) => v != null && v !== '')
    ).toString();
    return request(`/products?${q}`);
  },
  getProduct: (id) => request(`/products/${id}`),
  getCategories: () => request('/categories'),
  createOrder: (payload) => request('/orders', { method: 'POST', body: JSON.stringify(payload) }),
  getOrder: (id) => request(`/orders/${id}`),
};
