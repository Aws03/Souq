// ============================================================================
// اختيار المتغيّر في صفحة المنتج (V3، P-08c) — منطق خالص مُختبَر، بلا React ولا fetch.
//
// الخادم هو الحكم: الصفحة تحسب من نموذج التركيبات الذي أرسله (options + variants، والمعطّل ليس فيه أصلاً) وترسل
// معرّف المتغيّر؛ الخادم يعيد التحقّق من كل شيء (الملكية، النشاط، المتاح) ويرفض ما بطل. هذا الملف يمنع اختيار
// تركيبة يرفضها الخادم، لا أن يحلّ محلّه.
//
// القواعد الثابتة هنا:
//   • الاختيار صريح: لا قيمة تُنتقى تلقائياً حين للمتسوّق خيار حقيقي (P-08c).
//   • لا تبديل صامت: اختيار قيمة لا يغيّر قيمة خيار آخر اختارها المتسوّق أبداً.
//   • النافد يُعرض معطّلاً لا مخفياً (P-08c)؛ والمستحيل مع الاختيار الحالي معطّل كذلك، بسببه المعلن.
//   • القيم المخفيّة أصلاً (لا يستخدمها متغيّر نشط) لا يرسلها الخادم، فلا وجود لها هنا.
// ============================================================================
import { byPosition, combinationKey, nameIn, variantLabel } from './variantLabel';

/**
 * @typedef {{id:number, names:Record<string,string>}} OptionValue
 * @typedef {{id:number, position?:number, names:Record<string,string>, values:OptionValue[]}} ProductOption
 * @typedef {{id:number, optionValueIds:number[], price:number, compareAtPrice:number|null, available:number}} ProductVariant
 * @typedef {{options?:ProductOption[]|null, variants?:ProductVariant[]|null, price?:number,
 *            compareAtPrice?:number|null, stockQuantity?:number, priceIsFrom?:boolean} & Record<string, any>} StorefrontProduct
 * @typedef {Record<number, number>} Selection خيار ⇒ القيمة المختارة
 */

/** للمنتج خيارات ⇒ للمتسوّق ما يختاره. منتج بسيط (بلا خيارات) يعمل كما قبل V3 بلا أيّ اختيار. */
export const hasVariantChoice = (product) => (product?.options?.length ?? 0) > 0 && (product?.variants?.length ?? 0) > 0;

export const sortedOptions = (product) => [...(product?.options ?? [])].sort(byPosition);

const variantsOf = (product) => product?.variants ?? [];

/** الخيار الذي تنتمي إليه قيمة، أو null. */
const optionOfValue = (product, valueId) =>
  sortedOptions(product).find((option) => option.values.some((value) => value.id === valueId)) ?? null;

/**
 * الاختيار الابتدائي: من متغيّر يسمّيه الرابط (?variant=) إن كان ما زال معروضاً، وإلا من المتغيّر الوحيد حين لا
 * خيار حقيقي للمتسوّق، وإلا لا شيء. معرّف قديم أو لمتغيّر عُطّل أو لمنتج آخر يُتجاهَل بلا اختيار (لا حالة قديمة غير آمنة).
 * @param {StorefrontProduct|null} product
 * @param {number|string|null|undefined} variantId
 * @returns {Selection}
 */
export function initialSelection(product, variantId) {
  if (!hasVariantChoice(product)) return {};
  const variants = variantsOf(product);
  const requested = Number(variantId);
  const named = Number.isInteger(requested) && requested > 0
    ? variants.find((variant) => variant.id === requested)
    : null;
  // متغيّر واحد فقط ⇒ لا اختيار أمام المتسوّق: يُعرض مختاراً (ليس تبديلاً صامتاً — لا بديل له).
  const only = variants.length === 1 ? variants[0] : null;
  const variant = named ?? only;
  return variant ? selectionOf(product, variant) : {};
}

/** اختيار يمثّل متغيّراً بعينه. */
export function selectionOf(product, variant) {
  /** @type {Selection} */
  const selection = {};
  for (const valueId of variant?.optionValueIds ?? []) {
    const option = optionOfValue(product, valueId);
    if (option) selection[option.id] = valueId;
  }
  return selection;
}

/** اختيار قيمة: يغيّر خيارها وحده — قيم الخيارات الأخرى كما اختارها المتسوّق. */
export function selectValue(selection, optionId, valueId) {
  return { ...selection, [optionId]: valueId };
}

/** هل اكتمل الاختيار (قيمة لكل خيار)؟ */
export const isComplete = (product, selection) =>
  sortedOptions(product).every((option) => selection?.[option.id] != null);

/** المتغيّر المطابق لاختيار كامل، أو null (اختيار ناقص أو تركيبة لا متغيّر لها). */
export function variantFor(product, selection) {
  if (!isComplete(product, selection)) return null;
  const key = combinationKey(sortedOptions(product).map((option) => selection[option.id]));
  return variantsOf(product).find((variant) => combinationKey(variant.optionValueIds) === key) ?? null;
}

/**
 * حالة كل قيمة بالنظر إلى بقيّة الاختيار الحالي:
 *   selected    — هي المختارة في خيارها،
 *   possible    — يوجد متغيّر معروض يجمعها مع القيم المختارة في الخيارات الأخرى،
 *   purchasable — ومنه ما هو متاح الآن،
 *   reason      — 'soldOut' (تركيبة موجودة لكنها نفدت) أو 'unavailable' (لا تركيبة معها) أو null.
 * القيمة تُعطَّل حين لا تكون purchasable — ولا تُخفى، ولا يُبدَّل عنها اختيار المتسوّق.
 * @param {StorefrontProduct|null} product
 * @param {Selection} selection
 * @returns {Record<number, {selected:boolean, possible:boolean, purchasable:boolean, reason:'soldOut'|'unavailable'|null}>}
 */
export function valueStates(product, selection) {
  /** @type {Record<number, {selected:boolean, possible:boolean, purchasable:boolean, reason:'soldOut'|'unavailable'|null}>} */
  const states = {};
  const variants = variantsOf(product);

  for (const option of sortedOptions(product)) {
    // قيم الخيارات الأخرى وحدها: قيمة تُقاس مع ما اختاره المتسوّق في غير خيارها، لا مع نفسها.
    const others = Object.entries(selection ?? {})
      .filter(([optionId]) => Number(optionId) !== option.id)
      .map(([, valueId]) => valueId);

    for (const value of option.values) {
      const candidates = variants.filter((variant) =>
        variant.optionValueIds.includes(value.id) && others.every((other) => variant.optionValueIds.includes(other)));
      const purchasable = candidates.some((variant) => variant.available > 0);
      states[value.id] = {
        selected: selection?.[option.id] === value.id,
        possible: candidates.length > 0,
        purchasable,
        reason: purchasable ? null : (candidates.length > 0 ? 'soldOut' : 'unavailable'),
      };
    }
  }
  return states;
}

/** الخيارات التي لم تُختر بعد، بأسمائها بلغة الواجهة — لرسالة "اختر المقاس واللون". */
export const missingOptionNames = (product, selection, lang) =>
  sortedOptions(product).filter((option) => selection?.[option.id] == null).map((option) => nameIn(option.names, lang));

/** لا شيء من المنتج قابل للشراء الآن (كل متغيّراته المعروضة نفدت) — يبقى معروضاً غير متاح (قرار V3-b). */
export const allSoldOut = (product) =>
  hasVariantChoice(product) && variantsOf(product).every((variant) => variant.available <= 0);

/**
 * ما تعرضه صفحة المنتج الآن: السعر والمتاح ووصف المتغيّر وما يمنع الإضافة.
 * السعر من المتغيّر المختار حين اكتمل الاختيار، وإلا سعر المنتج كما حسبه الخادم ("ابتداءً من" حين تختلف الأسعار).
 * blocked: 'chooseOptions' (اختيار ناقص) أو 'soldOut' (المختار نفد) أو 'unavailable' (تركيبة بلا متغيّر) أو null.
 * @param {StorefrontProduct|null} product
 * @param {Selection} selection
 * @param {string} lang
 */
export function purchaseState(product, selection, lang) {
  if (!product) return { variant: null, price: 0, compareAtPrice: null, priceIsFrom: false, available: 0, label: '', blocked: 'unavailable' };

  if (!hasVariantChoice(product)) {
    const available = product.stockQuantity ?? 0;
    return {
      variant: null, price: product.price, compareAtPrice: product.compareAtPrice ?? null, priceIsFrom: false,
      available, label: '', blocked: available > 0 ? null : 'soldOut',
    };
  }

  const variant = variantFor(product, selection);
  if (!variant) {
    return {
      variant: null, price: product.price, compareAtPrice: product.compareAtPrice ?? null,
      priceIsFrom: !!product.priceIsFrom, available: 0, label: '',
      blocked: isComplete(product, selection) ? 'unavailable' : 'chooseOptions',
    };
  }
  return {
    variant, price: variant.price, compareAtPrice: variant.compareAtPrice ?? null, priceIsFrom: false,
    available: variant.available, label: variantLabel(product.options, variant.optionValueIds, lang),
    blocked: variant.available > 0 ? null : 'soldOut',
  };
}
