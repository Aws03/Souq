import { describe, expect, it } from 'vitest';
import {
  accountFormProblem, accountToForm, buildAccountPayload, buildRefundPayload, currencyDecimals, publishableMode, refundProblem,
  secretMode,
} from './paymentView';

const form = (overrides = {}) => ({ ...accountToForm(null), publishableKey: 'pk_test_51Habc', secretKey: 'sk_test_51Habc', ...overrides });

describe('store payment account form', () => {
  it('reads the mode of each kind of Stripe key and nothing else', () => {
    expect([publishableMode('pk_live_51Habc'), publishableMode(' pk_test_51Habc '), publishableMode('sk_live_51Habc')])
      .toEqual(['live', 'test', null]);
    expect([secretMode('sk_live_51Habc'), secretMode('rk_test_51Habc'), secretMode('pk_test_51Habc'), secretMode('sk_test_')])
      .toEqual(['live', 'test', null, null]);
  });

  it('reports the first problem that blocks saving', () => {
    expect(accountFormProblem(form({ publishableKey: 'sk_test_51Habc' }), false)).toBe('publishableInvalid');
    expect(accountFormProblem(form({ secretKey: '' }), false)).toBe('secretRequired');
    expect(accountFormProblem(form({ secretKey: '' }), true)).toBeNull();
    expect(accountFormProblem(form({ secretKey: 'password' }), false)).toBe('secretInvalid');
    expect(accountFormProblem(form({ publishableKey: 'pk_live_51Habc' }), false)).toBe('modeMismatch');
    expect(accountFormProblem(form({ webhookSecret: 'hook' }), false)).toBe('webhookInvalid');
    expect(accountFormProblem(form({ webhookSecret: 'whsec_abcd' }), false)).toBeNull();
  });

  it('never sends an empty secret, so the saved one is kept', () => {
    expect(buildAccountPayload(form({ secretKey: '  ', webhookSecret: '' }))).toEqual({ publishableKey: 'pk_test_51Habc' });
    expect(buildAccountPayload(form({ webhookSecret: ' whsec_abcd ' })))
      .toEqual({ publishableKey: 'pk_test_51Habc', secretKey: 'sk_test_51Habc', webhookSecret: 'whsec_abcd' });
  });
});

describe('refund form', () => {
  it('knows the minor units of the currency', () => {
    expect([currencyDecimals('JOD'), currencyDecimals('USD'), currencyDecimals('JPY')]).toEqual([3, 2, 0]);
  });

  it('refunds everything left when the amount is empty, and guards a typed amount', () => {
    expect(refundProblem('', 20, 3)).toBeNull();
    expect(refundProblem('', 0, 3)).toBe('nothingToRefund');
    expect(refundProblem('0', 20, 3)).toBe('amountInvalid');
    expect(refundProblem('abc', 20, 3)).toBe('amountInvalid');
    expect(refundProblem('20.001', 20, 3)).toBe('exceedsRefundable');
    expect(refundProblem('1.2345', 20, 3)).toBe('tooManyDecimals');
    expect(refundProblem('19.999', 20, 3)).toBeNull();
  });

  it('sends only what was typed', () => {
    expect(buildRefundPayload('', '  ')).toEqual({});
    expect(buildRefundPayload('5.5', ' تالف ')).toEqual({ amount: 5.5, reason: 'تالف' });
  });
});
