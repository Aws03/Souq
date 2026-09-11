// ============================================================================
// عملاء المتجر في لوحة الإدارة (المرحلة 7) — منطق خالص مُختبَر. الإجراءات تعكس قواعد الكيان (Customer.cs): المحذوف
// للعرض فقط، الإيقاف ورفعه بحسب الحالة، والتصدير والحذف بصلاحية customers.manage. الخادم يحرس كلاً منها أيضاً.
// ============================================================================
export const CUSTOMER_STATUSES = ['Active', 'Blocked'];

export function customerActions(customer, canManage) {
  if (!canManage || !customer || customer.isErased) return [];
  return [customer.status === 'Blocked' ? 'Unblock' : 'Block', 'Export', 'Erase'];
}

export const nextStatus = (action) => (action === 'Block' ? 'Blocked' : 'Active');

// القيم الفارغة تُحذف في toQueryString — لا "keyword=" يصل الخادم.
export const buildCustomerQuery = ({ keyword = '', status = '', page = 1, pageSize = 20 }) =>
  ({ keyword: keyword.trim(), status, page, pageSize });
