import { changeRatio } from './chartScales';

// ============================================================================
// تحويل استجابة اللوحة إلى ما تعرضه الشاشة — منطق خالص مُختبَر.
//
// القاعدة الحاكمة: لا رقم يُحسب هنا لم يُحسبه الخادم. ما في هذا الملف تنسيقٌ ومقارنةٌ
// وترتيب، لا محاسبة. الإيراد وصافيه ومتوسّط الطلب كلّها تصل محسوبة — لأن حسابها في مكانين
// يعني تعريفين للإيراد يفترقان يوماً.
// ============================================================================

/** حالات الطلب بترتيب دورة حياتها ورموز ألوانها — الترتيب هو المعنى لا الأبجدية. */
export const ORDER_STATUS_ORDER = ['Pending', 'Paid', 'Shipped', 'Delivered', 'Cancelled'];

const STATUS_TOKEN = {
  Pending: 'var(--color-accent)',
  Paid: 'var(--color-info)',
  Shipped: 'var(--color-primary)',
  Delivered: 'var(--color-success)',
  Cancelled: 'var(--color-danger)',
};

/** شرائح الحلقة بترتيب دورة الحياة، مع تسميات مترجَمة. */
export const statusSlices = (ordersByStatus, t) =>
  ORDER_STATUS_ORDER.map((status) => ({
    label: t(`orders.status.${status}`, { defaultValue: status }),
    value: ordersByStatus?.[status] ?? 0,
    color: STATUS_TOKEN[status],
  }));

/** مجموع الطلبات في المدّة عبر كل الحالات (بما فيها الملغاة — هذه حلقة "ماذا جرى" لا "ماذا رُبِح"). */
export const totalOrdersInPeriod = (ordersByStatus) =>
  ORDER_STATUS_ORDER.reduce((sum, status) => sum + (ordersByStatus?.[status] ?? 0), 0);

/**
 * بطاقات المؤشّرات مع مقارنتها بالمدّة السابقة.
 * كل بطاقة تحمل مفتاح صيغتها (formulaKey) كي تُعرض للمستخدم: رقم بلا تعريف رقمٌ يُساء فهمه،
 * و"الإيراد" و"صافي الإيراد" يختلفان بمقدار المُستردّ.
 */
export function kpiCards(dashboard) {
  if (!dashboard) return [];
  const { current, previous } = dashboard;

  return [
    { key: 'netRevenue', value: current.netRevenue, previous: previous.netRevenue, kind: 'money', formulaKey: 'netRevenue' },
    { key: 'orders', value: current.orders, previous: previous.orders, kind: 'count', formulaKey: 'orders' },
    { key: 'averageOrderValue', value: current.averageOrderValue, previous: previous.averageOrderValue, kind: 'money', formulaKey: 'averageOrderValue' },
    { key: 'newCustomers', value: current.newCustomers, previous: previous.newCustomers, kind: 'count', formulaKey: 'newCustomers' },
  ].map((card) => ({ ...card, change: changeRatio(card.value, card.previous) }));
}

/**
 * ما يحتاج تصرّفاً الآن، بترتيب الإلحاح. لا تنبيه بلا رقم: قائمة تنبيهات فارغة تعني متجراً
 * سليماً، وهي معلومة تُقال لا فراغ يُترك.
 */
export function operationalAlerts(dashboard) {
  if (!dashboard) return [];
  const alerts = [];
  if (dashboard.inventory?.outOfStock > 0)
    alerts.push({ key: 'outOfStock', count: dashboard.inventory.outOfStock, tone: 'danger', href: '/admin/inventory' });
  if (dashboard.pendingRefunds > 0)
    alerts.push({ key: 'pendingRefunds', count: dashboard.pendingRefunds, tone: 'danger', href: '/admin/payments' });
  if (dashboard.pendingOrders > 0)
    alerts.push({ key: 'pendingOrders', count: dashboard.pendingOrders, tone: 'warn', href: '/admin/orders' });
  if (dashboard.inventory?.low > 0)
    alerts.push({ key: 'lowStock', count: dashboard.inventory.low, tone: 'warn', href: '/admin/inventory' });
  return alerts;
}

/** نقاط المنحنى بالشكل الذي يقبله LineChart. */
export const trendPoints = (dashboard, field = 'revenue') =>
  (dashboard?.trend ?? []).map((point) => ({ label: point.bucket, value: point[field] ?? 0 }));

/** هل المتجر بلا أي نشاط تجاري في هذه المدّة؟ يقرّر عرض شاشة البداية بدل مخطّطات صفرية. */
export const hasNoActivity = (dashboard) =>
  !dashboard || (dashboard.current.orders === 0 && totalOrdersInPeriod(dashboard.ordersByStatus) === 0);

/** هل المتجر جديد تماماً (لا طلب ولا عميل ولا مخزون)؟ عندها الرسالة إرشاد لا تقرير. */
export const isBrandNewStore = (dashboard) =>
  hasNoActivity(dashboard)
  && (dashboard?.totalCustomers ?? 0) === 0
  && (dashboard?.inventory?.healthy ?? 0) + (dashboard?.inventory?.low ?? 0) + (dashboard?.inventory?.outOfStock ?? 0) === 0;
