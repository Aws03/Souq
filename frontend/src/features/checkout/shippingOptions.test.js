import { describe, expect, it } from 'vitest';
import { countryOfChoice, estimateLabel, pickShippingMethod, shippingProblem } from './shippingOptions';
import { NEW_ADDRESS } from './shippingChoice';

const t = (key, values) => `${key}:${JSON.stringify(values)}`;
const options = [{ methodId: 3 }, { methodId: 7 }];

describe('checkout shipping', () => {
  it('reads the country of a saved address, and none for a typed one', () => {
    const addresses = [{ id: 1, country: 'JO' }, { id: 2, country: 'SA' }];
    expect([countryOfChoice(addresses, 2), countryOfChoice(addresses, NEW_ADDRESS), countryOfChoice(null, 1)]).toEqual(['SA', null, null]);
  });

  it('keeps the chosen method while it is offered, otherwise the first one', () => {
    expect(pickShippingMethod(options, 7)).toBe(7);
    expect(pickShippingMethod(options, 99)).toBe(3);
    expect(pickShippingMethod(options, null)).toBe(3);
    expect(pickShippingMethod([], 7)).toBeNull();
  });

  it('words the delivery estimate', () => {
    expect(estimateLabel(1, 3, t)).toBe('checkout.shipping.daysRange:{"min":1,"max":3}');
    expect(estimateLabel(2, 2, t)).toBe('checkout.shipping.daysExact:{"days":2}');
    expect(estimateLabel(null, null, t)).toBeNull();
  });

  it('blocks the order only when the store ships and nothing usable is chosen', () => {
    expect(shippingProblem(null, null)).toBeNull();
    expect(shippingProblem({ required: false, options: [] }, null)).toBeNull();
    expect(shippingProblem({ required: true, options: [] }, null)).toBe('unavailable');
    expect(shippingProblem({ required: true, options }, null)).toBe('required');
    expect(shippingProblem({ required: true, options }, 3)).toBeNull();
  });
});
