import { useEffect, useState } from 'react';
import { CardElement, Elements, useElements, useStripe } from '@stripe/react-stripe-js';

import { api } from '../../api/client';
import Button from '../../components/common/Button';
import { ErrorBanner } from '../../components/common/StateViews';
import { getStripePromise } from './stripeClient';
import styles from './Checkout.module.css';

const CARD_ELEMENT_OPTIONS = {
  style: {
    base: { fontSize: '15px', fontFamily: 'Tajawal, sans-serif', color: '#1A2421', '::placeholder': { color: '#6b736f' } },
    invalid: { color: '#C4674E' },
  },
};

// نموذج الدفع الفعلي — يعمل داخل <Elements> فقط (useStripe/useElements يحتاجانها).
function InnerForm({ order, onPaid }) {
  const stripe = useStripe();
  const elements = useElements();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);

  const pay = async (e) => {
    e.preventDefault();
    if (!stripe || !elements) return;
    setBusy(true); setError(null);

    const { error: stripeError } = await stripe.confirmCardPayment(order.clientSecret, {
      payment_method: { card: elements.getElement(CardElement) },
    });
    if (stripeError) { setError(stripeError.message); setBusy(false); return; }

    try {
      const confirmed = await api.confirmOrderPayment(order.orderId);
      if (confirmed.status !== 'Paid') {
        setError('تعذّر تأكيد الدفع — حاول مرة أخرى.'); setBusy(false); return;
      }
      onPaid();
    } catch (err) { setError(err.message); setBusy(false); }
  };

  return (
    <form onSubmit={pay}>
      {error && <ErrorBanner message={error} />}
      <div className={styles.cardBox}><CardElement options={CARD_ELEMENT_OPTIONS} /></div>
      <Button type="submit" variant="saffron" size="lg" loading={busy} disabled={!stripe} className={styles.submit}>
        ادفع الآن
      </Button>
    </form>
  );
}

// خطوة الدفع: تنتظر تحميل Stripe.js (بمفتاحه من الخادم) ثم تعرض حقل البطاقة.
export default function CardPaymentForm({ order, onPaid }) {
  const [stripePromise, setStripePromise] = useState(undefined);

  useEffect(() => { getStripePromise().then(setStripePromise); }, []);

  if (stripePromise === undefined) return null;
  if (stripePromise === null)
    return <ErrorBanner message="الدفع عبر البطاقة غير مُهيّأ بعد على الخادم (مفتاح Stripe مفقود)." />;

  return (
    <div className={styles.panel}>
      <h2 className={styles.panelTitle}>بيانات البطاقة</h2>
      <Elements stripe={stripePromise}>
        <InnerForm order={order} onPaid={onPaid} />
      </Elements>
    </div>
  );
}
