import { loadStripe } from '@stripe/stripe-js';

import { api } from '../../api/client';

// وعد Stripe.js واحد يُعاد استخدامه (لا نحمّل السكربت أكثر من مرة). المفتاح العلني يأتي من الخادم بدل تضمينه ثابتاً
// في الواجهة.
//
// جوابان مختلفان لا يجوز خلطهما (TD-26):
//   • مفتاح فارغ = المتجر بلا بوّابة مهيّأة ⇒ null، وهي إجابة نهائية تُحفظ: التراجع لزرّ الإتمام المباشر صحيح هنا
//     (الخادم يستخدم البوّابة التجريبية).
//   • فشل الطلب (شبكة، 500، انقطاع) = لا نعرف ⇒ نرمي الخطأ ولا نحفظ الوعد، فالمحاولة التالية تسأل من جديد.
// خلطهما كان يحفظ الفشل للجلسة كلها: عطل لحظي واحد يُظهر للمشتري زرّ "ادفع الآن" بلا حقل بطاقة، فيؤكّد طلباً على نيّة
// دفع بلا وسيلة — والخادم يجيب الآن PaymentFailed قابلاً لإعادة المحاولة (ADR-0036). أي: عطل شبكة يتنكّر في صورة
// "متجر بلا Stripe".
let stripePromise;

export function getStripePromise() {
  if (!stripePromise) {
    stripePromise = api.getPaymentConfig()
      .then(({ publishableKey }) => (publishableKey ? loadStripe(publishableKey) : null))
      .catch((error) => { stripePromise = undefined; throw error; });
  }
  return stripePromise;
}
