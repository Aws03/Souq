import { describe, expect, it } from 'vitest';
import { buildMethodPayload, methodFormProblem, methodToForm, parseCountries } from './shippingForm';

const form = (overrides = {}) => ({ ...methodToForm(null), name: ' توصيل ', price: '2.5', ...overrides });

describe('shipping method form', () => {
  it('reads countries as distinct upper-case codes, with Arabic commas too', () => {
    expect(parseCountries(' jo, sa،JO  ae ')).toEqual(['JO', 'SA', 'AE']);
    expect(parseCountries('')).toEqual([]);
  });

  it('sends empty optional fields as null and the countries as a list', () => {
    expect(buildMethodPayload(form({ countries: 'jo, sa', minDays: '1', maxDays: '3' }))).toEqual({
      name: 'توصيل', price: 2.5, freeOverAmount: null, minDays: 1, maxDays: 3, carrier: null, trackingUrlTemplate: null,
      countries: ['JO', 'SA'], sortOrder: 0, isActive: true,
    });
  });

  it('fills the form from a method without leaking nulls into inputs', () => {
    expect(methodToForm({ name: 'X', price: 1, freeOverAmount: null, countries: ['JO', 'SA'], minDays: null }))
      .toMatchObject({ freeOverAmount: '', minDays: '', countries: 'JO, SA', carrier: '' });
  });

  it('reports the first problem that blocks saving', () => {
    expect(methodFormProblem(form({ name: ' ' }))).toBe('nameRequired');
    expect(methodFormProblem(form({ price: '' }))).toBe('priceInvalid');
    expect(methodFormProblem(form({ price: '-1' }))).toBe('priceInvalid');
    expect(methodFormProblem(form({ freeOverAmount: '0' }))).toBe('freeOverInvalid');
    expect(methodFormProblem(form({ minDays: '2' }))).toBe('estimateInvalid');
    expect(methodFormProblem(form({ minDays: '4', maxDays: '2' }))).toBe('estimateInvalid');
    expect(methodFormProblem(form({ trackingUrlTemplate: 'http://t.example/{number}' }))).toBe('trackingUrlInvalid');
    expect(methodFormProblem(form({ trackingUrlTemplate: 'https://t.example/track' }))).toBe('trackingUrlInvalid');
    expect(methodFormProblem(form({ countries: 'JOR' }))).toBe('countriesInvalid');
    expect(methodFormProblem(form({ price: '0', minDays: '1', maxDays: '1', trackingUrlTemplate: 'https://t.example/{number}' })))
      .toBeNull();
  });
});
