// ============================================================================
// وصف المتغيّر من قيم خياراته — قاعدة واحدة تستعملها واجهة المتجر (اختيار المتسوّق) ولوحة الإدارة (جدول المتغيّرات)،
// ونظيرتها على الخادم `VariantLabels.Compose` (ADR-0040): أسماء القيم بترتيب الخيارات مفصولة بـ " / "، كلٌّ بلغة
// الواجهة وإلا أول لغة متاحة بترتيب ثابت. منطق خالص مُختبَر، بلا React ولا fetch.
// ============================================================================

export const LABEL_SEPARATOR = ' / ';

/** الترتيب الذي اختاره التاجر (position)، والمعرّف كاسر تعادل — الخادم يرسل الاثنين. */
export const byPosition = (a, b) => (a.position ?? 0) - (b.position ?? 0) || a.id - b.id;

/**
 * @param {Record<string, string>|undefined} names
 * @param {string} lang
 */
export function nameIn(names, lang) {
  const value = names?.[lang]?.trim();
  if (value) return value;
  const fallback = Object.keys(names ?? {}).sort().map((culture) => names?.[culture]?.trim()).find(Boolean);
  return fallback ?? '';
}

/**
 * وصف تركيبة من معرّفات قيمها، بترتيب الخيارات لا بترتيب المعرّفات المُرسلة.
 * @param {{id:number, position?:number, values:{id:number, names:Record<string,string>}[]}[]} options
 * @param {number[]} valueIds
 * @param {string} lang
 */
export function variantLabel(options, valueIds, lang) {
  const ids = new Set(valueIds ?? []);
  return [...(options ?? [])].sort(byPosition)
    .map((option) => option.values.find((value) => ids.has(value.id)))
    .filter(Boolean)
    .map((value) => nameIn(value?.names, lang))
    .join(LABEL_SEPARATOR);
}

/** مفتاح تركيبة للمقارنة في الواجهة (المعرّفات مرتّبة) — الخادم يفرض تفرّدها بمفتاحه وفهرسه. */
export const combinationKey = (valueIds) => [...(valueIds ?? [])].sort((a, b) => a - b).join('.');

