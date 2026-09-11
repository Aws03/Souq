// ============================================================================
// معاملات جدول منتجات الإدارة → استعلام GET /api/admin/products (المرحلة 5: كل الحالات — مسودّة ونشط ومؤرشف).
// منطق خالص بلا عرض، مُختبَر بـ Vitest. هذه النقطة تربط فئة واحدة (categoryId) وحالة (status)؛ قائمة المتجر العامة
// هي التي تربط categoryIds المتكرّر (Phase 0 C8: مفتاح خاطئ يُتجاهَل بصمت، فالمفتاح هنا مُختبَر).
// ============================================================================
export function buildAdminProductQuery({ keyword, categoryId, status, page, pageSize }) {
  return {
    keyword: keyword?.trim() || undefined,
    categoryId: categoryId ? Number(categoryId) : undefined,
    status: status || undefined,
    page,
    pageSize,
  };
}
