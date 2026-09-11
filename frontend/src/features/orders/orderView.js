// ============================================================================
// عرض الطلبات (المرحلة 9) — منطق خالص مُختبَر: رابط التتبّع العام بالرمز، تسمية من غيّر الحالة في سجلّ الإدارة، ومرشّحات
// قائمة الإدارة كما يقبلها الخادم (القيم الفارغة تُحذف في toQueryString).
// ============================================================================
export const ORDER_STATUSES = ['Pending', 'Paid', 'Shipped', 'Delivered', 'Cancelled'];

export const trackingUrl = (origin, token) => `${String(origin).replace(/\/+$/, '')}/track/${token}`;

// موظّف باسمه، وإلا نوع الفاعل مترجماً (النظام، العميل، بوّابة الدفع). بلا فاعل (عرض العميل) ⇒ لا شيء.
export function actorLabel(entry, t) {
  if (!entry?.changedBy) return null;
  if (entry.changedBy === 'Staff' && entry.changedByName) return entry.changedByName;
  return t(`admin.orders.actor.${entry.changedBy}`, { defaultValue: entry.changedBy });
}

export const buildOrderQuery = ({ status = '', search = '', page = 1, pageSize = 20 }) =>
  ({ status, search: search.trim(), page, pageSize });
