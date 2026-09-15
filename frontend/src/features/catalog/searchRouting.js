// ============================================================================
// أين يعيش نصّ البحث؟ في الرابط.
//
// كان في حالة تخطيط المتجر، فكانت نتيجة البحث غير قابلة للمشاركة، وزرّ الرجوع لا يعيدها،
// وأي تنقّل يمحوها. لمتجر تجاري هذا عطل حقيقي: الزائر لا يستطيع إرسال نتيجة بحث لأحد،
// والرجوع من صفحة منتج يفقد ما بحث عنه.
//
// الحساب هنا خالص كي يُختبَر بلا متصفّح؛ الربط في useStoreSearch.
// ============================================================================

// الصفحات التي تعرض كتالوجاً فتفهم ?q= في مكانها.
export const CATALOG_ROUTES = ['/', '/offers'];

export const isCatalogRoute = (pathname) => CATALOG_ROUTES.includes(pathname);

// من صفحة كتالوج: عدّل الرابط في مكانه. من غيرها (صفحة منتج، سلّة): انتقل إلى الرئيسية
// بالبحث — البحث فعل تنقّل، لا تعديل حالة في صفحة لا تعرض نتائج.
export function searchDestination(pathname, search, query) {
  const params = new URLSearchParams(isCatalogRoute(pathname) ? search : '');
  const trimmed = (query ?? '').trim();
  if (trimmed) params.set('q', trimmed);
  else params.delete('q');
  // أي بحث جديد يبدأ من الصفحة الأولى؛ نتائج مختلفة كلياً.
  params.delete('page');

  const target = isCatalogRoute(pathname) ? pathname : '/';
  const queryString = params.toString();
  return { pathname: target, search: queryString ? `?${queryString}` : '' };
}

// تعديل في مكانه (نفس الصفحة) أم تنقّل حقيقي يضيف مدخلاً في تاريخ المتصفّح؟
export const isInPlace = (pathname, destination) => pathname === destination.pathname;
