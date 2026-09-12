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
// الخادم يستخدم FakeGateway الذي يؤكّد الدفع تلقائياً، فنعرض زرّ إتمام
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
// ثلاث نتائج لا نتيجتان (TD-26): مفتاح ⇒ حقل البطاقة؛ بلا مفتاح ⇒ تراجع صامت لزرّ الإتمام المباشر (المتجر بلا بوّابة
// مهيّأة، والخادم يستخدم البوّابة التجريبية)؛ وتعذّر السؤال ⇒ خطأ بإعادة محاولة. الأخيرة كانت تُخلط بالثانية فيُعرض
// للمشتري زرّ دفع بلا حقل بطاقة بسبب عطل شبكة لحظي.
export default function CardPaymentForm({ order, onPaid }) {
  const { t } = useTranslation();
  const [attempt, setAttempt] = useState(0);
  const [gateway, setGateway] = useState({ status: 'loading' });

  useEffect(() => {
    let active = true;
    setGateway({ status: 'loading' });
    getStripePromise()
      .then((stripe) => { if (active) setGateway({ status: 'ready', stripe }); })
      .catch((e) => { if (active) setGateway({ status: 'failed', message: e.message }); });
    return () => { active = false; };
  }, [attempt]);

  if (gateway.status === 'loading') return null;

  if (gateway.status === 'failed') {
    return (
      <div className={styles.panel}>
        <h2 className={styles.panelTitle}>{t('checkout.cardTitle')}</h2>
        <ErrorBanner message={gateway.message} onRetry={() => setAttempt((n) => n + 1)} />
      </div>
    );
  }

  if (gateway.stripe === null) return <DirectPayForm order={order} onPaid={onPaid} />;

  return (
    <div className={styles.panel}>
      <h2 className={styles.panelTitle}>{t('checkout.cardTitle')}</h2>
      <Elements stripe={gateway.stripe}>
        <InnerForm order={order} onPaid={onPaid} />
      </Elements>
    </div>
  );
}
