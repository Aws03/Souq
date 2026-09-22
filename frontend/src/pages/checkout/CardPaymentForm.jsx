import { useEffect, useMemo, useState } from 'react';
import { CardElement, Elements, useElements, useStripe } from '@stripe/react-stripe-js';
import { useTranslation } from 'react-i18next';

import { api } from '../../api/client';
import Button from '../../components/common/Button';
import Skeleton from '../../components/common/Skeleton';
import { ErrorBanner } from '../../components/common/StateViews';
import { useStoreConfig } from '../../app/TenantProvider';
import { fontStylesheetUrl } from '../../app/tenantModel';
import { cardAppearance, cardFonts, readCssVariable } from '../../features/checkout/cardAppearance';
import { getStripePromise } from './stripeClient';
import styles from './Checkout.module.css';

// نموذج الدفع الفعلي — يعمل داخل <Elements> فقط (useStripe/useElements يحتاجانها).
function InnerForm({ order, onPaid }) {
  const { t } = useTranslation();
  const stripe = useStripe();
  const elements = useElements();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);

  const options = useMemo(() => cardAppearance(readCssVariable), []);

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
      <div className={styles.cardBox}><CardElement options={options} /></div>
      <Button type="submit" variant="accent" size="lg" loading={busy} disabled={!stripe} className={styles.submit}>
        {t('checkout.payNow')}
      </Button>
    </form>
  );
}

// ============================================================================
// الدفعُ التجريبي (ADR-0063) — ويُعلَن، لا يُخفى.
//
// كان هذا المسار **تراجعاً صامتاً**: بلا مفاتيح مزوّدٍ على الخادم يظهر زرُّ إتمامٍ عاديّ، ولا
// شيء يقول للمستخدم إنّ لا مالاً يتحرّك. وهو مقبولٌ في بيئة تطوير، **وغيرُ مقبولٍ في عرضٍ
// عامّ**: شاشةُ دفعٍ لا تقول إنّها تجريبية تدّعي ضمناً أنّها ليست كذلك.
//
// فصار يُعلن نفسه، ويشرح كيف تُستدعى المسارات الأخرى: المبلغُ هو ما يختار النتيجة، فيستطيع
// مَن يراجع المشروع أن يُظهر الرفضَ والانتظارَ والإلغاء بتغيير الكمّية وحدها.
// ============================================================================
function DemoPayForm({ order, onPaid }) {
  const { t } = useTranslation();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);

  const pay = async () => {
    setBusy(true); setError(null);
    try {
      const confirmed = await api.confirmOrderPayment(order.orderId);
      // حالةٌ غير «مدفوع» بلا خطأ: لا تقع اليوم، وتُقال صراحةً إن وقعت بدل أن تُبتلع.
      if (confirmed.status !== 'Paid') { setError(t('checkout.confirmFailed')); setBusy(false); return; }
      onPaid();
    } catch (err) {
      // رسالةُ الخادم كما هي: «قيد المعالجة»، «بطاقة مرفوضة»، «أُلغيت» — ثلاثتها مسارات
      // يعرضها المحوّل التجريبي عمداً، وعرضُها برسالةٍ واحدة عامّة يُخفي ما وُجدت لتُظهره.
      setError(err.message); setBusy(false);
    }
  };

  return (
    <div className={styles.panel}>
      <h2 className={styles.panelTitle}>{t('checkout.completeOrderTitle')}</h2>

      <div className={styles.demoNotice} role="note">
        <strong>{t('checkout.demo.title')}</strong>
        <p>{t('checkout.demo.body')}</p>
        <p className={styles.demoHint}>{t('checkout.demo.outcomes')}</p>
      </div>

      {error && <ErrorBanner message={error} />}
      <Button variant="accent" size="lg" loading={busy} onClick={pay} className={styles.submit}>
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
  const config = useStoreConfig();
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

  // فراغ أبيض مكان حقل البطاقة كان يبدو عطلاً في أكثر خطوة يقلق فيها المشتري.
  if (gateway.status === 'loading') {
    return (
      <div className={styles.panel}>
        <h2 className={styles.panelTitle}>{t('checkout.cardTitle')}</h2>
        <Skeleton height={120} radius={12} />
      </div>
    );
  }

  if (gateway.status === 'failed') {
    return (
      <div className={styles.panel}>
        <h2 className={styles.panelTitle}>{t('checkout.cardTitle')}</h2>
        <ErrorBanner message={gateway.message} onRetry={() => setAttempt((n) => n + 1)} />
      </div>
    );
  }

  if (gateway.stripe === null) return <DemoPayForm order={order} onPaid={onPaid} />;

  const storeFonts = cardFonts(fontStylesheetUrl(config?.settings?.branding?.typography));

  // خطّ المتجر يُحمَّل داخل إطار Stripe أيضاً: تسميته وحدها لا تكفي، والإطار لا يرى أوراق أنماط صفحتنا.
  return (
    <div className={styles.panel}>
      <h2 className={styles.panelTitle}>{t('checkout.cardTitle')}</h2>
      <Elements stripe={gateway.stripe} options={{ fonts: storeFonts }}>
        <InnerForm order={order} onPaid={onPaid} />
      </Elements>
    </div>
  );
}
