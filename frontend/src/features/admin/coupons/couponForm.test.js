import { describe, expect, it } from 'vitest';
import { buildCouponPayload, couponFormProblem, couponToForm } from './couponForm';

const form = (overrides = {}) => ({ ...couponToForm(null), code: ' save10 ', value: '10', ...overrides });

describe('coupon form', () => {
  it('sends the window and per-customer limit, with empty fields as null', () => {
    expect(buildCouponPayload(form({ startsAt: '2026-10-01', maxUsesPerCustomer: '2' }), false)).toEqual({
      code: 'SAVE10', type: 'Percentage', value: 10, minOrderAmount: null,
      startsAt: '2026-10-01T00:00:00.000Z', expiresAt: null, maxUses: null, maxUsesPerCustomer: 2, isActive: true,
    });
  });

  it('never sends the code when editing', () => {
    expect(buildCouponPayload(form(), true)).not.toHaveProperty('code');
  });

  it('fills the form from a coupon without leaking nulls into inputs', () => {
    const filled = couponToForm({ code: 'X', type: 'FixedAmount', value: 5, startsAt: '2026-10-01T00:00:00Z', maxUsesPerCustomer: null });

    expect(filled).toMatchObject({ startsAt: '2026-10-01', expiresAt: '', maxUsesPerCustomer: '', minOrderAmount: '' });
  });

  it('reports the first problem that blocks saving', () => {
    expect(couponFormProblem(form({ code: ' ' }), false)).toBe('codeRequired');
    expect(couponFormProblem(form({ code: ' ' }), true)).toBeNull();
    expect(couponFormProblem(form({ value: '0' }), false)).toBe('valueInvalid');
    expect(couponFormProblem(form({ startsAt: '2026-10-05', expiresAt: '2026-10-01' }), false)).toBe('windowInvalid');
    expect(couponFormProblem(form({ maxUses: '1', maxUsesPerCustomer: '2' }), false)).toBe('perCustomerTooHigh');
    expect(couponFormProblem(form({ startsAt: '2026-10-01', expiresAt: '2026-10-05', maxUses: '5', maxUsesPerCustomer: '1' }), false)).toBeNull();
  });
});
