// ============================================================================
// نموذج الفئة وشجرتها في لوحة الإدارة (المرحلة 5) — منطق خالص مُختبَر بـ Vitest. الخادم يحرس الحلقات والعمق
// (5 مستويات)؛ هنا نعرض الشجرة مرتّبة، ونخفي من خيارات "الفئة الأب" الفئة نفسها وفروعها كي لا يصل المدير لخطأ متوقَّع.
// ============================================================================
import { formToTexts } from '../../catalog/catalogText';

// ترتيب عرض شجري: كل أب يليه أبناؤه (بترتيب العرض ثم المعرّف)، مع عمق كل فئة للإزاحة.
export function orderAsTree(categories) {
  const ids = new Set(categories.map((c) => c.id));
  const children = new Map();
  for (const category of categories) {
    const parent = ids.has(category.parentId) ? category.parentId : null;   // أب خارج القائمة ⇒ جذر
    if (!children.has(parent)) children.set(parent, []);
    children.get(parent).push(category);
  }
  for (const list of children.values()) list.sort((a, b) => a.sortOrder - b.sortOrder || a.id - b.id);

  const ordered = [];
  const seen = new Set();
  const visit = (parentId, depth) => {
    for (const category of children.get(parentId) ?? []) {
      if (seen.has(category.id)) continue;
      seen.add(category.id);
      ordered.push({ ...category, depth });
      visit(category.id, depth + 1);
    }
  };
  visit(null, 0);
  // دفاعي: صفوف لا تصل إليها الشجرة (حلقة في بيانات قديمة) تبقى ظاهرة ليصلحها المدير.
  for (const category of categories) if (!seen.has(category.id)) ordered.push({ ...category, depth: 0 });
  return ordered;
}

// كل فروع فئة (أبناؤها وأبناؤهم...).
export function descendantIds(categories, id) {
  const found = new Set();
  const pending = [id];
  while (pending.length) {
    const current = pending.pop();
    for (const category of categories) {
      if (category.parentId === current && !found.has(category.id)) {
        found.add(category.id);
        pending.push(category.id);
      }
    }
  }
  return found;
}

export function buildCategoryPayload(form) {
  return {
    slug: form.slug.trim().toLowerCase(),
    translations: formToTexts(form.texts),
    parentId: form.parentId ? Number(form.parentId) : null,
    sortOrder: Number(form.sortOrder) || 0,
    isActive: !!form.isActive,
  };
}

// التفعيل/التعطيل من الجدول: PUT يستبدل الفئة كاملة، فنعيد حقولها كما هي مع العلم الجديد.
export function activationPayload(category, isActive) {
  return {
    slug: category.slug,
    translations: category.translations,
    parentId: category.parentId ?? null,
    sortOrder: category.sortOrder ?? 0,
    isActive,
  };
}
