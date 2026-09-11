import { describe, expect, it } from 'vitest';
import { toApiError } from './problem';

const translations = { InvalidCoupon: 'This coupon cannot be used', ValidationFailed: 'Some values are invalid' };
const translate = (code) => translations[code] ?? null;

describe('toApiError', () => {
  const problem = {
    title: 'Unprocessable Entity', status: 422, code: 'InvalidCoupon',
    detail: 'انتهت صلاحية الكوبون', traceId: '4bf92f3577b34da6a3ce929d0e0e4736',
  };

  it('keeps status, code and traceId for logic and support', () => {
    const error = toApiError(422, problem, { translate });

    expect(error).toBeInstanceOf(Error);
    expect(error.status).toBe(422);
    expect(error.code).toBe('InvalidCoupon');
    expect(error.traceId).toBe('4bf92f3577b34da6a3ce929d0e0e4736');
  });

  it('shows the precise server message when the UI speaks the server language', () => {
    expect(toApiError(422, problem, { translate, preferServerDetail: true }).message).toBe('انتهت صلاحية الكوبون');
  });

  it('translates the stable code when the UI speaks another language', () => {
    expect(toApiError(422, problem, { translate, preferServerDetail: false }).message).toBe('This coupon cannot be used');
  });

  it('falls back to the server message for codes the UI does not know yet', () => {
    const unknown = { ...problem, code: 'SomethingNew' };
    expect(toApiError(422, unknown, { translate, preferServerDetail: false }).message).toBe('انتهت صلاحية الكوبون');
  });

  it('exposes field errors and uses the first one when there is no detail', () => {
    const validation = { status: 400, code: 'ValidationFailed', errors: { email: ['البريد غير صالح'] } };
    const error = toApiError(400, validation, { preferServerDetail: true });

    expect(error.fieldErrors).toEqual({ email: ['البريد غير صالح'] });
    expect(error.message).toBe('البريد غير صالح');
  });

  it('uses the fallback message when the body is missing or not JSON', () => {
    const error = toApiError(502, null, { translate, fallbackMessage: 'Connection error' });

    expect(error.message).toBe('Connection error');
    expect(error.code).toBeNull();
    expect(error.traceId).toBeNull();
  });
});
