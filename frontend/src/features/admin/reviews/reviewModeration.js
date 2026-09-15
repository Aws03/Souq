// ============================================================================
// شاشة الإشراف على التقييمات (المرحلة 13): الحالات، وسلسلة استعلام الطابور، والإجراءات المتاحة لكل حالة — المعلّق يُعتمد أو
// يُرفض، المعتمد يُرفض فيختفي من المتجر، المرفوض يُعاد اعتماده. الخادم يحرس الصلاحية والمتجر في كل الأحوال. منطق خالص مُختبَر.
// ============================================================================
export const REVIEW_STATUSES = ['Pending', 'Approved', 'Rejected'];

// ملاحظة الرفض (Review.ModerationNoteMaxLength).
export const NOTE_MAX_LENGTH = 500;

// صنف شارة الحالة في Admin.module.css.
export const STATUS_BADGE = { Pending: 'pending', Approved: 'delivered', Rejected: 'cancelled' };

/**
 * @param {{status?: string, productId?: number|string, page?: number, pageSize?: number}} [filters]
 * @returns {{page: number, pageSize: number, status?: string, productId?: number}}
 */
export function buildReviewQuery({ status, productId, page = 1, pageSize = 20 } = {}) {
  /** @type {{page: number, pageSize: number, status?: string, productId?: number}} */
  const query = { page, pageSize };
  if (REVIEW_STATUSES.includes(status)) query.status = status;
  const id = Number(productId);
  if (Number.isInteger(id) && id > 0) query.productId = id;
  return query;
}

export function moderationActions(review) {
  switch (review?.status) {
    case 'Pending': return ['approve', 'reject'];
    case 'Approved': return ['reject'];
    case 'Rejected': return ['approve'];
    default: return [];
  }
}
