// ============================================================================
// خيارات المنتج ومتغيّراته في الإدارة (ADR-0040) — منطق خالص مُختبَر بـ Vitest، بلا React ولا fetch.
//
// الخادم هو الحكم: الحدود تأتي منه (variantLimits في نموذج المنتج) لا من هنا، والتحقّق هنا لتجربة المدير فقط —
// رسالة واضحة قبل الإرسال — ثم تُقرأ رموز الخادم الثابتة إن رفض. المعرّفات والتركيبات التي تُرسَل تُفحص على الخادم
// داخل المنتج نفسه (قيمة منتج آخر، تركيبة مكرّرة، قيمة مستخدمة) مهما قال هذا الملف.
// ============================================================================
import { CATALOG_CULTURES } from '../../catalog/catalogText';

/**
 * @typedef {Record<string, string>} Names
 * @typedef {{ id: number, position: number, names: Names }} OptionValue
 * @typedef {{ id: number, position: number, names: Names, values: OptionValue[] }} ProductOption
 * @typedef {{ id: number, isDefault: boolean, isActive: boolean, sku: string|null, price: number,
 *             compareAtPrice: number|null, optionValueIds: number[], onHand: number, reserved: number,
 *             available: number, lowStockThreshold: number }} ProductVariant
 * @typedef {{ maxOptions: number, maxValuesPerOption: number, maxVariants: number, nameMaxLength: number,
 *             skuMaxLength: number }} VariantLimits
 * @typedef {{ options: ProductOption[], variants: ProductVariant[], variantLimits: VariantLimits,
 *             price: number, currency: string }} AdminProduct
 * @typedef {{ key: string, id: number|null, names: Names }} ValueForm
 * @typedef {{ key: string, id: number|null, names: Names, values: ValueForm[], existingVariantsValue: string|null }} OptionForm
 */

export const LABEL_SEPARATOR = ' / ';

let nextKey = 0;
const newKey = () => `new-${++nextKey}`;

const emptyNames = () => Object.fromEntries(CATALOG_CULTURES.map((culture) => [culture, '']));

/** @param {Names|undefined} names @returns {Names} */
const namesToForm = (names) => ({ ...emptyNames(), ...(names ?? {}) });

/**
 * الاسم بلغة الواجهة، وإلا أول لغة متاحة بترتيب ثابت — القاعدة نفسها على الخادم (VariantLabels).
 * @param {Names|undefined} names
 * @param {string} lang
 */
export function nameIn(names, lang) {
  const value = names?.[lang]?.trim();
  if (value) return value;
  const fallback = Object.keys(names ?? {}).sort().map((culture) => names?.[culture]?.trim()).find(Boolean);
  return fallback ?? '';
}

// ── نموذج الخيارات ──────────────────────────────────────────────────────────

/** @param {ProductOption[]|undefined} options @returns {OptionForm[]} */
export function optionsToForm(options) {
  return [...(options ?? [])].sort((a, b) => a.position - b.position).map((option) => ({
    key: `option-${option.id}`,
    id: option.id,
    names: namesToForm(option.names),
    values: [...option.values].sort((a, b) => a.position - b.position)
      .map((value) => ({ key: `value-${value.id}`, id: value.id, names: namesToForm(value.names) })),
    existingVariantsValue: null,
  }));
}

/** @returns {OptionForm} */
export function newOption() {
  const firstValue = newValue();
  return { key: newKey(), id: null, names: emptyNames(), values: [firstValue], existingVariantsValue: firstValue.key };
}

/** @returns {ValueForm} */
export function newValue() {
  return { key: newKey(), id: null, names: emptyNames() };
}

/** @param {Names} names */
const trimmedNames = (names) => Object.fromEntries(
  Object.entries(names).map(([culture, name]) => [culture, name?.trim() ?? '']).filter(([, name]) => name),
);

/**
 * نموذج الخيارات ⇒ جسم PUT options: الترتيب ترتيب المصفوفة، اللغة الفارغة لا تُرسل، وخيار جديد يسمّي موضع القيمة
 * التي تأخذها المتغيّرات القائمة (القيمة المختارة، وإلا الأولى).
 * @param {OptionForm[]} form
 */
export function buildOptionsPayload(form) {
  return {
    options: form.map((option) => {
      const values = option.values.map((value) => ({ id: value.id, names: trimmedNames(value.names) }));
      if (option.id !== null) return { id: option.id, names: trimmedNames(option.names), values };
      const chosen = option.values.findIndex((value) => value.key === option.existingVariantsValue);
      return { id: null, names: trimmedNames(option.names), values, existingVariantsValue: Math.max(chosen, 0) };
    }),
  };
}

/**
 * أول مشكلة في النموذج (مفتاح ترجمة وقيمه) أو null. مرآة لقواعد الخادم كي يعرف المدير قبل الإرسال — لا بديل عنها.
 * @param {OptionForm[]} form
 * @param {VariantLimits} limits
 * @param {string} defaultCulture لغة المتجر الافتراضية: الاسم بها شرط
 * @returns {{ key: string, values?: Record<string, unknown> } | null}
 */
export function validateOptionsForm(form, limits, defaultCulture) {
  if (form.length > limits.maxOptions) return { key: 'tooManyOptions', values: { max: limits.maxOptions } };

  const seenOptions = [];
  for (const option of form) {
    const names = trimmedNames(option.names);
    const label = nameIn(names, defaultCulture);
    if (!names[defaultCulture]) return { key: 'optionNameRequired', values: { culture: defaultCulture } };
    if (Object.values(names).some((name) => name.length > limits.nameMaxLength)) {
      return { key: 'nameTooLong', values: { max: limits.nameMaxLength } };
    }
    if (seenOptions.some((other) => clash(other, names))) return { key: 'duplicateOptionName', values: { name: label } };
    seenOptions.push(names);

    if (option.values.length === 0) return { key: 'valuesRequired', values: { option: label } };
    if (option.values.length > limits.maxValuesPerOption) {
      return { key: 'tooManyValues', values: { option: label, max: limits.maxValuesPerOption } };
    }
    const seenValues = [];
    for (const value of option.values) {
      const valueNames = trimmedNames(value.names);
      if (!valueNames[defaultCulture]) return { key: 'valueNameRequired', values: { option: label, culture: defaultCulture } };
      if (Object.values(valueNames).some((name) => name.length > limits.nameMaxLength)) {
        return { key: 'nameTooLong', values: { max: limits.nameMaxLength } };
      }
      if (seenValues.some((other) => clash(other, valueNames))) {
        return { key: 'duplicateValue', values: { option: label, value: nameIn(valueNames, defaultCulture) } };
      }
      seenValues.push(valueNames);
    }
  }
  return null;
}

// اسمان متطابقان في لغة مشتركة بلا اعتبار لحالة الأحرف — كما على الخادم.
function clash(a, b) {
  return Object.entries(a).some(([culture, name]) => b[culture] && b[culture].toLocaleLowerCase() === name.toLocaleLowerCase());
}

/**
 * قيم يستخدمها متغيّر (نشطاً أو معطّلاً): لا تُحذف — المتغيّرات لا تُحذف، ولكلٍّ تركيبته كاملة.
 * @param {ProductVariant[]} variants
 */
export function usedValueIds(variants) {
  return new Set((variants ?? []).flatMap((variant) => variant.optionValueIds));
}

// ── المتغيّرات ──────────────────────────────────────────────────────────────

/**
 * وصف المتغيّر بلغة الواجهة: قيمه بترتيب الخيارات ("M / أحمر")؛ فارغ لمنتج بلا خيارات.
 * @param {ProductOption[]} options
 * @param {number[]} valueIds
 * @param {string} lang
 */
export function variantLabel(options, valueIds, lang) {
  const ids = new Set(valueIds);
  return [...(options ?? [])].sort((a, b) => a.position - b.position)
    .map((option) => option.values.find((value) => ids.has(value.id)))
    .filter(Boolean)
    .map((value) => nameIn(value?.names, lang))
    .join(LABEL_SEPARATOR);
}

/**
 * مفتاح تركيبة للمقارنة في الواجهة (المعرّفات مرتّبة) — الخادم يفرض تفرّدها بمفتاحه وفهرسه.
 * @param {number[]} valueIds
 */
export const combinationKey = (valueIds) => [...valueIds].sort((a, b) => a - b).join('.');

/**
 * التركيبات التي لا متغيّر لها بعد (المعطّل يُعدّ موجوداً)، بترتيب الخيارات والقيم — أساس "أنشئ التركيبات الناقصة".
 * @param {ProductOption[]} options
 * @param {ProductVariant[]} variants
 * @returns {number[][]}
 */
export function missingCombinations(options, variants) {
  const ordered = [...(options ?? [])].sort((a, b) => a.position - b.position);
  if (ordered.length === 0) return [];
  const existing = new Set((variants ?? []).map((variant) => combinationKey(variant.optionValueIds)));

  /** @type {number[][]} */
  let combinations = [[]];
  for (const option of ordered) {
    const values = [...option.values].sort((a, b) => a.position - b.position);
    combinations = combinations.flatMap((prefix) => values.map((value) => [...prefix, value.id]));
  }
  return combinations.filter((ids) => !existing.has(combinationKey(ids)));
}

/**
 * كم متغيّراً يمكن إضافته بعد (الحدّ يعدّ المعطّل أيضاً).
 * @param {AdminProduct} product
 */
export const remainingVariantCapacity = (product) =>
  Math.max(product.variantLimits.maxVariants - product.variants.length, 0);

const optionalNumber = (value) => (value === '' || value === null || value === undefined ? null : Number(value));

/**
 * جسم POST variants لتركيبات مختارة بتسعير ومخزون مشتركين (يُعدَّل كلٌّ منها بعد الإنشاء).
 * @param {number[][]} combinations
 * @param {{ price: string|number, compareAtPrice?: string|number|null, initialStock?: string|number,
 *           lowStockThreshold?: string|number, isActive: boolean }} shared
 */
export function buildVariantsPayload(combinations, shared) {
  const threshold = optionalNumber(shared.lowStockThreshold);
  return {
    variants: combinations.map((optionValueIds) => ({
      optionValueIds,
      price: Number(shared.price),
      compareAtPrice: optionalNumber(shared.compareAtPrice),
      initialStock: Number(shared.initialStock) || 0,
      ...(threshold !== null ? { lowStockThreshold: threshold } : {}),
      isActive: shared.isActive,
    })),
  };
}

/**
 * جسم PUT variants/{id}: السعر وسعر المقارنة وSKU (مقصوص، والفارغ null).
 * @param {{ price: string|number, compareAtPrice?: string|number|null, sku?: string|null }} form
 */
export function buildVariantPricingPayload(form) {
  return {
    price: Number(form.price),
    compareAtPrice: optionalNumber(form.compareAtPrice),
    sku: form.sku?.trim() || null,
  };
}

/**
 * مشكلة تسعير قبل الإرسال (مفتاح ترجمة) أو null.
 * @param {{ price: string|number, compareAtPrice?: string|number|null }} form
 */
export function validateVariantPricing(form) {
  if (form.price === '' || !(Number(form.price) > 0)) return 'priceInvalid';
  const compareAt = optionalNumber(form.compareAtPrice);
  if (compareAt !== null && compareAt <= Number(form.price)) return 'compareAtInvalid';
  return null;
}

/**
 * واجهة المتجر تعرض المنتج حتى يُبنى اختيار المتغيّر فقط إن كان له متغيّر نشط واحد (بوّابة V2 على الخادم) — للتنبيه.
 * @param {ProductVariant[]} variants
 */
export const hiddenFromStorefront = (variants) => (variants ?? []).filter((variant) => variant.isActive).length > 1;
