// ============================================================================
// معاملات قائمة المنتجات في لوحة الإدارة → استعلام الـ API. منطق خالص بلا عرض،
// مُختبَر بـ Vitest. الـ API يربط الفئات من مفتاح categoryIds المتكرّر (List<int>) —
// كانت الشاشة ترسل categoryId فيُتجاهَل الفلتر بصمت (Phase 0 C8).
// ============================================================================
export function buildAdminProductQuery({ keyword, categoryId, page, pageSize }) {
  return {
    keyword: keyword?.trim() || undefined,
    categoryIds: categoryId ? [Number(categoryId)] : undefined,
    page,
    pageSize,
  };
}
