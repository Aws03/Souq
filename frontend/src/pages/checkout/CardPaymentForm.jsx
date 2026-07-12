import { useEffect, useState } from 'react';
import { CardElement, Elements, useElements, useStripe } from '@stripe/react-stripe-js';
import { useTranslation } from 'react-i18next';

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
  const { t } = useTranslation();
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
        setError(t('checkout.confirmFailed')); setBusy(false); return;
      }
      onPaid();
    } catch (err) { setError(err.message); setBusy(false); }
  };

  return (
    <form onSubmit={pay}>
      {error && <ErrorBanner message={error} />}
      <div className={styles.cardBox}><CardElement options={CARD_ELEMENT_OPTIONS} /></div>
      <Button type="submit" variant="saffron" size="lg" loading={busy} disabled={!stripe} className={styles.submit}>
        {t('checkout.payNow')}
      </Button>
    </form>
  );
}

// تراجع صامت حين لا يكون Stripe مُهيّأً على الخادم: لا تحذير للمستخدم إطلاقاً —
// الخادم يستخدم FakePaymentService الذي يؤكّد الدفع تلقائياً، فنعرض زرّ إتمام
// مباشراً يستدعي confirm-payment (يمرّ عبر البوّابة التجريبية وينجح). المتجر
// يبقى قابلاً للاستخدام كاملاً بلا مفاتيح Stripe.
function DirectPayForm({ order, onPaid }) {
  const { t } = useTranslation();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);

  const pay = async () => {
    setBusy(true); setError(null);
    try {
      const confirmed = await api.confirmOrderPayment(order.orderId);
      if (confirmed.status !== 'Paid') { setError(t('checkout.confirmFailed')); setBusy(false); return; }
      onPaid();
    } catch (err) { setError(err.message); setBusy(false); }
  };

  return (
    <div className={styles.panel}>
      <h2 className={styles.panelTitle}>{t('checkout.completeOrderTitle')}</h2>
      {error && <ErrorBanner message={error} />}
      <Button variant="saffron" size="lg" loading={busy} onClick={pay} className={styles.submit}>
        {t('checkout.payNow')}
      </Button>
    </div>
  );
}

// خطوة الدفع: تنتظر تحميل Stripe.js (بمفتاحه من الخادم) ثم تعرض حقل البطاقة.
// إن غاب المفتاح (Stripe غير مُهيّأ) نتراجع صامتين لزرّ الإتمام المباشر بلا تحذير.
export default function CardPaymentForm({ order, onPaid }) {
  const { t } = useTranslation();
  const [stripePromise, setStripePromise] = useState(undefined);

  useEffect(() => { getStripePromise().then(setStripePromise); }, []);

  if (stripePromise === undefined) return null;
  if (stripePromise === null) return <DirectPayForm order={order} onPaid={onPaid} />;

  return (
    <div className={styles.panel}>
      <h2 className={styles.panelTitle}>{t('checkout.cardTitle')}</h2>
      <Elements stripe={stripePromise}>
        <InnerForm order={order} onPaid={onPaid} />
      </Elements>
    </div>
  );
}
