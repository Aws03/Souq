import { describe, expect, it } from 'vitest';
import { addressToForm, emptyAddress, formToAddress, formatAddressLine, missingAddressFields } from './addressForm';

describe('address form', () => {
  it('sends a trimmed address with an upper-case country and null optional fields', () => {
    const payload = formToAddress({
      ...emptyAddress(), recipientName: ' سارة ', phone: ' 0790000000 ', country: 'jo', city: ' عمّان ', line1: ' شارع 1 ', region: '  ',
    });

    expect(payload).toEqual({
      recipientName: 'سارة', phone: '0790000000', country: 'JO', city: 'عمّان', line1: 'شارع 1',
      region: null, line2: null, postalCode: null, label: null,
    });
  });

  it('lists the required fields that are still empty', () => {
    expect(missingAddressFields({ ...emptyAddress(), recipientName: 'سارة', country: '' }))
      .toEqual(['phone', 'country', 'city', 'line1']);
    expect(missingAddressFields(addressToForm({ recipientName: 'س', phone: '079', country: 'JO', city: 'ع', line1: 'ش' }))).toEqual([]);
  });

  it('fills the form from a saved address without leaking nulls into inputs', () => {
    const form = addressToForm({ id: 3, recipientName: 'سارة', region: null, isDefaultShipping: true });

    expect(form.region).toBe('');
    expect(form).not.toHaveProperty('id');
    expect(form.country).toBe('JO');
  });

  it('formats a one-line summary skipping empty parts', () => {
    expect(formatAddressLine({ line1: 'شارع 1', line2: null, city: 'عمّان', region: '', country: 'JO' })).toBe('شارع 1، عمّان، JO');
  });
});
