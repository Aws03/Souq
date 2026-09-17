import { describe, expect, it } from 'vitest';
import {
  buildOptionsPayload, buildVariantPricingPayload, buildVariantsPayload, hiddenFromStorefront, missingCombinations, nameIn,
  newOption, newValue, optionsToForm, remainingVariantCapacity, usedValueIds, validateOptionsForm, validateVariantPricing,
  variantLabel,
} from './variantModel';

const limits = { maxOptions: 3, maxValuesPerOption: 20, maxVariants: 100, nameMaxLength: 50, skuMaxLength: 64 };

// مقاس (S, M) ولون (أحمر، أزرق) — المواضع معكوسة عمداً في المصفوفة: الترتيب من position لا من ترتيب JSON.
const options = [
  { id: 2, position: 1, names: { ar: 'اللون', en: 'Colour' }, values: [
    { id: 21, position: 1, names: { ar: 'أزرق', en: 'Blue' } },
    { id: 20, position: 0, names: { ar: 'أحمر' } },
  ] },
  { id: 1, position: 0, names: { ar: 'المقاس', en: 'Size' }, values: [
    { id: 10, position: 0, names: { ar: 'S', en: 'S' } },
    { id: 11, position: 1, names: { ar: 'M', en: 'M' } },
  ] },
];

const variant = (id, optionValueIds, overrides = {}) => ({
  id, isDefault: false, isActive: true, sku: null, price: 10, compareAtPrice: null, optionValueIds,
  onHand: 0, reserved: 0, available: 0, lowStockThreshold: 5, ...overrides,
});

describe('variantLabel', () => {
  it('joins the values in option order in the interface language, falling back to another language', () => {
    expect(variantLabel(options, [21, 11], 'en')).toBe('M / Blue');
    expect(variantLabel(options, [20, 10], 'en')).toBe('S / أحمر');
    expect(variantLabel(options, [20, 10], 'ar')).toBe('S / أحمر');
  });

  it('is empty for a product without options', () => {
    expect(variantLabel([], [], 'ar')).toBe('');
  });
});

describe('nameIn', () => {
  it('prefers the requested language, then the first available one in a stable order', () => {
    expect(nameIn({ ar: 'مقاس', en: 'Size' }, 'en')).toBe('Size');
    expect(nameIn({ en: 'Size', ar: 'مقاس' }, 'fr')).toBe('مقاس');
    expect(nameIn({ ar: '  ', en: 'Size' }, 'ar')).toBe('Size');
  });
});

describe('missingCombinations', () => {
  it('lists every combination without a variant, counting inactive variants as existing', () => {
    const missing = missingCombinations(options, [variant(1, [10, 20]), variant(2, [21, 11], { isActive: false })]);

    expect(missing).toEqual([[10, 21], [11, 20]]);
  });

  it('has nothing to create for a product without options', () => {
    expect(missingCombinations([], [variant(1, [])])).toEqual([]);
  });
});

describe('options form', () => {
  it('round-trips existing options by id and orders them by position', () => {
    const form = optionsToForm(options);

    expect(form.map((o) => o.names.ar)).toEqual(['المقاس', 'اللون']);
    expect(buildOptionsPayload(form).options[1]).toEqual({
      id: 2, names: { ar: 'اللون', en: 'Colour' },
      values: [{ id: 20, names: { ar: 'أحمر' } }, { id: 21, names: { ar: 'أزرق', en: 'Blue' } }],
    });
    expect(buildOptionsPayload(form).options[0]).not.toHaveProperty('existingVariantsValue');
  });

  it('sends a new option with trimmed names and the position of the value existing variants take', () => {
    const option = newOption();
    option.names = { ar: ' المادة ', en: '' };
    const cotton = { ...newValue(), names: { ar: 'قطن', en: 'Cotton' } };
    const linen = { ...newValue(), names: { ar: 'كتّان', en: ' ' } };
    option.values = [cotton, linen];
    option.existingVariantsValue = linen.key;

    expect(buildOptionsPayload([option]).options[0]).toEqual({
      id: null, names: { ar: 'المادة' },
      values: [{ id: null, names: { ar: 'قطن', en: 'Cotton' } }, { id: null, names: { ar: 'كتّان' } }],
      existingVariantsValue: 1,
    });
  });

  it('falls back to the first value when the chosen one was removed', () => {
    const option = newOption();
    option.existingVariantsValue = 'gone';

    expect(buildOptionsPayload([option]).options[0].existingVariantsValue).toBe(0);
  });
});

describe('validateOptionsForm', () => {
  const valid = () => optionsToForm(options);

  it('accepts the current definition', () => {
    expect(validateOptionsForm(valid(), limits, 'ar')).toBeNull();
  });

  it('mirrors the published limits', () => {
    const tooMany = [...valid(), newOption(), newOption()];
    expect(validateOptionsForm(tooMany, limits, 'ar')?.key).toBe('tooManyOptions');

    const values = valid();
    values[0].values = Array.from({ length: 21 }, (_, i) => ({ ...newValue(), names: { ar: `v${i}`, en: '' } }));
    expect(validateOptionsForm(values, limits, 'ar')).toEqual({ key: 'tooManyValues', values: { option: 'المقاس', max: 20 } });

    const long = valid();
    long[0].names.en = 'x'.repeat(51);
    expect(validateOptionsForm(long, limits, 'ar')?.key).toBe('nameTooLong');
  });

  it('requires names in the store default language and refuses duplicates in any language, ignoring case', () => {
    const missingDefault = valid();
    missingDefault[0].names.ar = ' ';
    expect(validateOptionsForm(missingDefault, limits, 'ar')?.key).toBe('optionNameRequired');

    const duplicateOption = valid();
    duplicateOption[1].names.en = 'SIZE';
    expect(validateOptionsForm(duplicateOption, limits, 'ar')?.key).toBe('duplicateOptionName');

    const duplicateValue = valid();
    duplicateValue[0].values[1].names = { ar: 's', en: '' };
    expect(validateOptionsForm(duplicateValue, limits, 'ar')?.key).toBe('duplicateValue');

    const noValues = valid();
    noValues[1].values = [];
    expect(validateOptionsForm(noValues, limits, 'ar')?.key).toBe('valuesRequired');

    const unnamedValue = valid();
    unnamedValue[0].values.push(newValue());
    expect(validateOptionsForm(unnamedValue, limits, 'ar')?.key).toBe('valueNameRequired');
  });
});

describe('variants', () => {
  it('knows which values are used, including by inactive variants', () => {
    expect([...usedValueIds([variant(1, [10, 20]), variant(2, [11, 20], { isActive: false })])].sort()).toEqual([10, 11, 20]);
  });

  it('builds one variant per chosen combination with shared pricing and opening stock', () => {
    expect(buildVariantsPayload([[10, 21], [11, 20]], { price: '12.500', compareAtPrice: '', initialStock: '3', lowStockThreshold: '', isActive: false }))
      .toEqual({ variants: [
        { optionValueIds: [10, 21], price: 12.5, compareAtPrice: null, initialStock: 3, isActive: false },
        { optionValueIds: [11, 20], price: 12.5, compareAtPrice: null, initialStock: 3, isActive: false },
      ] });
    expect(buildVariantsPayload([[10]], { price: 5, initialStock: '', lowStockThreshold: '0', isActive: true }).variants[0])
      .toMatchObject({ initialStock: 0, lowStockThreshold: 0 });
  });

  it('validates and builds a variant pricing edit', () => {
    expect(validateVariantPricing({ price: '0' })).toBe('priceInvalid');
    expect(validateVariantPricing({ price: '' })).toBe('priceInvalid');
    expect(validateVariantPricing({ price: '10', compareAtPrice: '10' })).toBe('compareAtInvalid');
    expect(validateVariantPricing({ price: '10', compareAtPrice: '12' })).toBeNull();
    expect(buildVariantPricingPayload({ price: '10.25', compareAtPrice: '', sku: ' tee-m ' }))
      .toEqual({ price: 10.25, compareAtPrice: null, sku: 'tee-m' });
  });

  it('reports remaining capacity against the published limit and the temporary storefront gate', () => {
    const product = { options, variants: [variant(1, [10, 20]), variant(2, [11, 20], { isActive: false })], variantLimits: limits, price: 10, currency: 'JOD' };

    expect(remainingVariantCapacity(product)).toBe(98);
    expect(hiddenFromStorefront(product.variants)).toBe(false);
    expect(hiddenFromStorefront([...product.variants, variant(3, [10, 21])])).toBe(true);
  });
});
