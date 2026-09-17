import { describe, expect, it } from 'vitest';
import { MAX_QUANTITY, couponProblemMessage, hasProblems, lineProblem, nextQuantity, toCartItems } from './basketModel';

const basket = {
  currency: 'JOD',
  lines: [
    { productId: 5, variantId: 50, name: 'سماعات', translations: { ar: { name: 'سماعات' } }, imageUrl: '/a.png',
      unitPrice: 12.5, quantity: 2, lineTotal: 25, sellable: true, available: 4,
      variantLabel: 'M / أحمر', variantLabels: { ar: 'M / أحمر', en: 'M / Red' } },
    { productId: 6, variantId: 60, name: 'شاحن', translations: {}, imageUrl: null,
      unitPrice: 3, quantity: 1, lineTotal: 3, sellable: false, available: 0 },
  ],
};

describe('basket model', () => {
  it('maps server lines to the cart item shape the components render', () => {
    const [first] = toCartItems(basket, 'ar');

    expect(first).toEqual({
      id: 5, variantId: 50, name: 'سماعات', translations: { ar: { name: 'سماعات' } }, imageUrl: '/a.png',
      price: 12.5, qty: 2, lineTotal: 25, currency: 'JOD', sellable: true, available: 4, variantLabel: 'M / أحمر',
    });
    // سطر لمنتج بلا خيارات: لا وصف (المنتج البسيط كما كان قبل V3).
    expect(toCartItems(basket, 'ar')[1].variantLabel).toBeNull();
    // وبلغة الواجهة حين يعرفها المنتج، وإلا لقطة لغة المتجر.
    expect(toCartItems(basket, 'en')[0].variantLabel).toBe('M / Red');
    expect(toCartItems({ ...basket, lines: [{ ...basket.lines[0], variantLabels: null }] }, 'en')[0].variantLabel).toBe('M / أحمر');
    expect(toCartItems(null)).toEqual([]);
  });

  it('flags lines that would block checkout', () => {
    const items = toCartItems(basket, 'ar');

    expect(lineProblem(items[0])).toBeNull();
    expect(lineProblem(items[1])).toEqual({ code: 'unavailable' });
    expect(lineProblem({ ...items[0], qty: 5 })).toEqual({ code: 'onlyLeft', count: 4 });
    expect(hasProblems(items)).toBe(true);
    expect(hasProblems([items[0]])).toBe(false);
  });

  it('keeps quantity changes within the line limit, zero meaning removal', () => {
    expect(nextQuantity({ qty: 1 }, -1)).toBe(0);
    expect(nextQuantity({ qty: 2 }, 1)).toBe(3);
    expect(nextQuantity({ qty: MAX_QUANTITY }, 1)).toBe(MAX_QUANTITY);
  });

  it('explains a rejected coupon in the UI language', () => {
    const rejected = { code: 'OLD', applied: false, errorCode: 'InvalidCoupon', message: 'انتهت صلاحية الكوبون' };
    const translate = (code) => (code === 'InvalidCoupon' ? "This coupon can't be used" : null);

    expect(couponProblemMessage(rejected, { translate, preferServerDetail: true })).toBe('انتهت صلاحية الكوبون');
    expect(couponProblemMessage(rejected, { translate, preferServerDetail: false })).toBe("This coupon can't be used");
    expect(couponProblemMessage({ ...rejected, errorCode: 'Other' }, { translate, preferServerDetail: false }))
      .toBe('انتهت صلاحية الكوبون');
    expect(couponProblemMessage({ code: 'OK', applied: true }, { translate, preferServerDetail: false })).toBeNull();
    expect(couponProblemMessage(null, { translate, preferServerDetail: false })).toBeNull();
  });
});
