// ============================================================================
// نصّ الإشعار ورابطه (المرحلة 14): الخادم يرسل النوع ومعاملات صغيرة (رقم الطلب، الحالة، اسم المنتج) لا نصّاً — فيُعرض بلغة
// الزائر ويتبع تبديلها. إشعارات الإدارة (طلب جديد، مخزون ينفد) تقود لصفحات اللوحة، وإشعار العميل لصفحة طلبه. منطق خالص مُختبَر.
// ============================================================================
export const POLL_MS = 60_000;

export function badgeLabel(count) {
  if (!count || count < 1) return '';
  return count > 99 ? '99+' : String(count);
}

export function describeNotification(notification, t) {
  const data = notification?.data ?? {};
  switch (notification?.kind) {
    case 'order.status':
      return {
        text: t('notifications.orderStatus', {
          number: data.orderNumber,
          status: t(`orders.status.${data.status}`, { defaultValue: data.status }),
        }),
        link: data.orderId ? `/orders/${data.orderId}` : null,
      };
    case 'order.new':
      return { text: t('notifications.newOrder', { number: data.orderNumber }), link: '/admin/orders' };
    case 'stock.low':
      // وصف المتغيّر ("M / أحمر") يرسله الخادم لمنتج بخيارات فقط (ADR-0040)؛ الإشعارات الأقدم بلا وصف كما كانت.
      return {
        text: data.variantLabel
          ? t('notifications.lowStockVariant', { name: data.productName, variant: data.variantLabel, available: data.available })
          : t('notifications.lowStock', { name: data.productName, available: data.available }),
        link: '/admin/inventory',
      };
    default:
      return { text: t('notifications.generic'), link: null };
  }
}
