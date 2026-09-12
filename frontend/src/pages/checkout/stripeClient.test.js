import { beforeEach, describe, expect, it, vi } from 'vitest';

// البوّابة الحقيقية لا تُحمَّل في الاختبار؛ يكفي أن نعرف أنّها استُدعيت وبأي مفتاح.
vi.mock('@stripe/stripe-js', () => ({ loadStripe: vi.fn(async (key) => ({ loadedWith: key })) }));
vi.mock('../../api/client', () => ({ api: { getPaymentConfig: vi.fn() } }));

let getStripePromise;
let api;
let loadStripe;

beforeEach(async () => {
  vi.resetModules();                       // الوعد محفوظ داخل الوحدة: نسخة جديدة لكل اختبار
  ({ api } = await import('../../api/client'));
  ({ loadStripe } = await import('@stripe/stripe-js'));
  ({ getStripePromise } = await import('./stripeClient'));

  // resetModules يُجدّد الوحدة لا بدائلها: عدّادات vi.fn تتراكم بين الاختبارات ما لم تُمسح هنا. mockReset لدالة
  // الإعداد (لا سلوك افتراضي لها)، وmockClear لـ loadStripe كي يبقى تنفيذها من المصنع.
  api.getPaymentConfig.mockReset();
  loadStripe.mockClear();
});

describe('the Stripe availability cache', () => {
  it('does not cache a failed request, so the next attempt asks again (TD-26)', async () => {
    api.getPaymentConfig.mockRejectedValueOnce(new Error('connection lost'));

    await expect(getStripePromise()).rejects.toThrow('connection lost');

    // المحاولة الثانية تسأل الخادم من جديد بدل أن ترث فشل الأولى للجلسة كلها.
    api.getPaymentConfig.mockResolvedValueOnce({ publishableKey: 'pk_live_1' });
    await expect(getStripePromise()).resolves.toEqual({ loadedWith: 'pk_live_1' });

    expect(api.getPaymentConfig).toHaveBeenCalledTimes(2);
  });

  it('caches a real key and loads Stripe.js only once', async () => {
    api.getPaymentConfig.mockResolvedValue({ publishableKey: 'pk_live_1' });

    const first = await getStripePromise();
    const second = await getStripePromise();

    expect(second).toBe(first);
    expect(api.getPaymentConfig).toHaveBeenCalledTimes(1);
    expect(loadStripe).toHaveBeenCalledTimes(1);
  });

  it('caches an empty key as "this store has no gateway" — a real answer, not a failure', async () => {
    api.getPaymentConfig.mockResolvedValue({ publishableKey: '' });

    await expect(getStripePromise()).resolves.toBeNull();
    await expect(getStripePromise()).resolves.toBeNull();

    expect(api.getPaymentConfig).toHaveBeenCalledTimes(1);
    expect(loadStripe).not.toHaveBeenCalled();
  });
});
