import { loadStripe } from '@stripe/stripe-js';

import { api } from '../../api/client';

// وعد Stripe.js واحد يُعاد استخدامه (لا نحمّل السكربت أكثر من مرة). المفتاح
// العلني يأتي من الخادم بدل تضمينه ثابتاً في الواجهة — لو غاب (لا Stripe مضبوطاً
// على الخادم بعد) نُعيد null بدل رمي خطأ يكسر الصفحة.
let stripePromise;

export function getStripePromise() {
  if (!stripePromise) {
    stripePromise = api.getPaymentConfig()
      .then(({ publishableKey }) => (publishableKey ? loadStripe(publishableKey) : null))
      .catch(() => null);
  }
  return stripePromise;
}
