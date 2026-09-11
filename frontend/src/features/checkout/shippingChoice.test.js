import { describe, expect, it } from 'vitest';
import { NEW_ADDRESS, initialShippingChoice, isShippingChoiceMissing, shippingPayload } from './shippingChoice';

describe('checkout shipping choice', () => {
  it('preselects the default shipping address, then the first, then a new address', () => {
    expect(initialShippingChoice([{ id: 4 }, { id: 9, isDefaultShipping: true }])).toBe(9);
    expect(initialShippingChoice([{ id: 4 }, { id: 9 }])).toBe(4);
    expect(initialShippingChoice([])).toBe(NEW_ADDRESS);
    expect(initialShippingChoice(null)).toBe(NEW_ADDRESS);
  });

  it('sends only the saved address id, never its text', () => {
    expect(shippingPayload('9', 'ignored')).toEqual({ shippingAddress: null, shippingAddressId: 9 });
  });

  it('sends the trimmed free-text address for a new address', () => {
    expect(shippingPayload(NEW_ADDRESS, '  عمّان، شارع 1 ')).toEqual({ shippingAddress: 'عمّان، شارع 1', shippingAddressId: null });
  });

  it('requires text only when a new address is chosen', () => {
    expect(isShippingChoiceMissing(NEW_ADDRESS, '   ')).toBe(true);
    expect(isShippingChoiceMissing(NEW_ADDRESS, 'عمّان')).toBe(false);
    expect(isShippingChoiceMissing(9, '')).toBe(false);
  });
});
