import { describe, expect, it } from 'vitest';
import {
  allSoldOut, hasVariantChoice, initialSelection, isComplete, missingOptionNames, purchaseState, selectValue,
  selectionOf, sortedOptions, valueStates, variantFor,
} from './variantSelection';

// مقاس (S, M) × لون (أحمر، أزرق) — المواضع معكوسة في المصفوفة عمداً: الترتيب من position لا من ترتيب JSON.
const options = [
  { id: 2, position: 1, names: { ar: 'اللون', en: 'Colour' }, values: [
    { id: 21, names: { ar: 'أحمر', en: 'Red' } },
    { id: 22, names: { ar: 'أزرق', en: 'Blue' } },
  ] },
  { id: 1, position: 0, names: { ar: 'المقاس', en: 'Size' }, values: [
    { id: 11, names: { ar: 'S', en: 'S' } },
    { id: 12, names: { ar: 'M', en: 'M' } },
  ] },
];

const variant = (id, optionValueIds, available, price = 20) =>
  ({ id, optionValueIds, price, compareAtPrice: null, available });

// S/أحمر متاح، S/أزرق نفد، M/أحمر متاح بسعر أعلى — وM/أزرق تركيبة لا متغيّر لها (لم يُنشئها التاجر).
const product = (overrides = {}) => ({
  id: 7, slug: 'tee', price: 20, compareAtPrice: null, currency: 'JOD', stockQuantity: 9, priceIsFrom: true,
  options,
  variants: [variant(71, [11, 21], 5), variant(72, [11, 22], 0), variant(73, [12, 21], 4, 25)],
  ...overrides,
});

const simple = { id: 8, slug: 'cable', price: 5, compareAtPrice: null, stockQuantity: 3, options: null, variants: null };

describe('shape', () => {
  it('a product without options offers no choice, and its options sort by position', () => {
    expect(hasVariantChoice(simple)).toBe(false);
    expect(hasVariantChoice(product())).toBe(true);
    expect(sortedOptions(product()).map((o) => o.names.en)).toEqual(['Size', 'Colour']);
  });
});

describe('initialSelection', () => {
  it('takes the variant the link names', () => {
    expect(initialSelection(product(), 73)).toEqual({ 1: 12, 2: 21 });
    // متغيّر نفد ما زال اختياراً حقيقياً: الرابط يفتحه، والصفحة تقول إنه نفد.
    expect(initialSelection(product(), '72')).toEqual({ 1: 11, 2: 22 });
  });

  it('ignores a stale, foreign or withdrawn variant id and selects nothing', () => {
    expect(initialSelection(product(), 999)).toEqual({});
    expect(initialSelection(product(), 'abc')).toEqual({});
    expect(initialSelection(product(), null)).toEqual({});
  });

  it('selects nothing when the shopper has a real choice (explicit selection, P-08c)', () => {
    expect(initialSelection(product(), undefined)).toEqual({});
  });

  it('preselects the only variant when there is nothing to choose between', () => {
    const one = product({ variants: [variant(71, [11, 21], 5)] });
    expect(initialSelection(one, undefined)).toEqual({ 1: 11, 2: 21 });
  });

  it('offers no selection at all for a product without options', () => {
    expect(initialSelection(simple, 5)).toEqual({});
  });
});

describe('selectValue', () => {
  it('changes its own option only — never another option the shopper chose', () => {
    const selection = selectionOf(product(), variant(73, [12, 21], 4));
    expect(selectValue(selection, 2, 22)).toEqual({ 1: 12, 2: 22 });
    expect(selectValue({}, 1, 11)).toEqual({ 1: 11 });
  });
});

describe('variantFor', () => {
  it('matches a complete selection whatever the order of its ids', () => {
    expect(variantFor(product(), { 1: 11, 2: 21 })?.id).toBe(71);
    expect(variantFor(product(), { 2: 21, 1: 12 })?.id).toBe(73);
  });

  it('is null for an incomplete selection or a combination that has no variant', () => {
    expect(variantFor(product(), { 1: 11 })).toBeNull();
    expect(variantFor(product(), { 1: 12, 2: 22 })).toBeNull();
    expect(isComplete(product(), { 1: 12 })).toBe(false);
    expect(isComplete(product(), { 1: 12, 2: 22 })).toBe(true);
  });
});

describe('valueStates', () => {
  it('with nothing selected, a value is purchasable when any of its variants has stock', () => {
    const states = valueStates(product(), {});

    expect(states[11]).toMatchObject({ purchasable: true, possible: true, reason: null });   // S: أحمر متاح
    expect(states[12]).toMatchObject({ purchasable: true, possible: true });                 // M: أحمر متاح
    expect(states[21]).toMatchObject({ purchasable: true, possible: true });                 // أحمر في المقاسين
    expect(states[22]).toMatchObject({ purchasable: false, possible: true, reason: 'soldOut' }); // أزرق: S نفد وM لا تركيبة
  });

  it('narrows the other option as the shopper chooses, without hiding valid values', () => {
    const states = valueStates(product(), { 1: 11 });     // المقاس S

    expect(states[21]).toMatchObject({ purchasable: true, reason: null });
    expect(states[22]).toMatchObject({ purchasable: false, possible: true, reason: 'soldOut' });   // S/أزرق نفد
    // القيم في خيار المقاس تبقى كما هي: قياسها مع لون لم يُختر بعد لا مع نفسها.
    expect(states[12]).toMatchObject({ purchasable: true, possible: true });
  });

  it("marks a value impossible with the current selection rather than switching the shopper's choice", () => {
    const states = valueStates(product(), { 2: 22 });     // أزرق

    expect(states[11]).toMatchObject({ possible: true, purchasable: false, reason: 'soldOut' });
    expect(states[12]).toMatchObject({ possible: false, purchasable: false, reason: 'unavailable' });
    expect(states[22].selected).toBe(true);
  });

  it('keeps the selected value marked even when it is sold out', () => {
    expect(valueStates(product(), { 1: 11, 2: 22 })[22]).toMatchObject({ selected: true, purchasable: false });
  });
});

describe('purchaseState', () => {
  it('shows the product "from" price until the selection is complete, and asks for the missing options', () => {
    const state = purchaseState(product(), { 1: 11 }, 'en');

    expect(state).toMatchObject({ variant: null, price: 20, priceIsFrom: true, available: 0, blocked: 'chooseOptions' });
    expect(missingOptionNames(product(), { 1: 11 }, 'en')).toEqual(['Colour']);
  });

  it('shows the selected variant price, availability and label once complete', () => {
    const state = purchaseState(product(), { 1: 12, 2: 21 }, 'ar');

    expect(state).toMatchObject({ price: 25, available: 4, blocked: null, priceIsFrom: false });
    expect(state.variant?.id).toBe(73);
    expect(state.label).toBe('M / أحمر');
  });

  it('blocks a sold-out variant and a combination that has no variant', () => {
    expect(purchaseState(product(), { 1: 11, 2: 22 }, 'en')).toMatchObject({ blocked: 'soldOut', available: 0, price: 20 });
    expect(purchaseState(product(), { 1: 12, 2: 22 }, 'en')).toMatchObject({ blocked: 'unavailable', variant: null });
  });

  it('keeps a product without options exactly as before: no label, stock from the product', () => {
    expect(purchaseState(simple, {}, 'en')).toMatchObject({ price: 5, available: 3, label: '', blocked: null, priceIsFrom: false });
    expect(purchaseState({ ...simple, stockQuantity: 0 }, {}, 'en').blocked).toBe('soldOut');
  });

  it('reports a product whose every shown variant is sold out', () => {
    const none = product({ variants: [variant(71, [11, 21], 0), variant(72, [11, 22], 0)] });

    expect(allSoldOut(none)).toBe(true);
    expect(allSoldOut(product())).toBe(false);
    expect(allSoldOut(simple)).toBe(false);
    expect(valueStates(none, {})[11]).toMatchObject({ purchasable: false, reason: 'soldOut' });
  });

  it('handles a missing product without throwing', () => {
    expect(purchaseState(null, {}, 'en').blocked).toBe('unavailable');
  });
});
