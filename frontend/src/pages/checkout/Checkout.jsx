import { useCallback, useEffect, useRef, useState } from 'react';
import { useNavigate, useOutletContext } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useCart } from '../../context/CartContext';
import { api } from '../../api/client';
import {
  NEW_ADDRESS, initialShippingChoice, isShippingChoiceMissing, shippingPayload,
} from '../../features/checkout/shippingChoice';
import { countryOfChoice, pickShippingMethod, shippingProblem } from '../../features/checkout/shippingOptions';
import { ErrorBanner, EmptyState } from '../../components/common/StateViews';
import Skeleton from '../../components/common/Skeleton';
import { couponProblemMessage, hasProblems } from '../../features/basket/basketModel';
import { PackageIcon } from '../../components/icons/Icons';
import AddressStep from './AddressStep';
import CardPaymentForm from './CardPaymentForm';
import OrderSummaryPanel from './OrderSummaryPanel';
import styles from './Checkout.module.css';

// صفحة الدفع (محمية: تتطلّب تسجيل الدخول) — خطوتان:
//  1) عنوان الشحن + طريقة الشحن (المرحلة 12) + كوبون اختياري → ينشئ الطلب على الخادم (يحجز المخزون وينشئ نيّة دفع)
//     ويعيد ClientSecret. التسعير كله من الخادم (/basket/quote) بالخطّ نفسه الذي يُنشئ الطلب: ما يُعرض هو ما سيُدفع.
//  2) بطاقة حقيقية عبر Stripe Elements (لا تصل تفاصيلها خادمنا إطلاقاً) → تأكيد لدى الخادم يتحقّق من النتيجة.
export default function Checkout() {
  const { t, i18n } = useTranslation();
  const { basket, items, total, loaded, loadFailed, reload } = useCart();
  const { refreshProducts } = useOutletContext();
  const navigate = useNavigate();

  const [savedAddresses, setSavedAddresses] = useState(null); // null حتى يُحمَّل دفتر العناوين
  const [shippingChoice, setShippingChoice] = useState(NEW_ADDRESS);
  const [address, setAddress] = useState('');
  const [addressTouched, setAddressTouched] = useState(false);
  const [shippingMethodId, setShippingMethodId] = useState(null);
  const [quote, setQuote] = useState(null); // آخر تسعير من الخادم (الكوبون المطبَّق + الشحن)
  const [couponCode, setCouponCode] = useState('');
  const [couponPreview, setCouponPreview] = useState(null);
  const [couponError, setCouponError] = useState(null);
  const [couponBusy, setCouponBusy] = useState(false);
  const [order, setOrder] = useState(null);
  const [busy, setBusy] = useState(false);
  const [serverError, setServerError] = useState(null);

  const currency = basket.currency;
  const blocked = hasProblems(items);
  const appliedCode = useRef(null);
  const addressError = isShippingChoiceMissing(shippingChoice, address) ? t('checkout.addressRequired') : null;
  const country = countryOfChoice(savedAddresses, shippingChoice);
  const shipping = quote?.shippingMethods ?? null;
  const shippingIssue = shippingProblem(shipping, shippingMethodId);

  // دفتر العناوين (المرحلة 7): الافتراضي للشحن مختار مبدئياً. تعذّر تحميله ⇒ عنوان نصّي كما قبل.
  useEffect(() => {
    api.getMyAddresses()
      .then((list) => { setSavedAddresses(list); setShippingChoice(initialShippingChoice(list)); })
      .catch(() => setSavedAddresses([]));
  }, []);

  // تسعير الخادم للكوبون وطريقة الشحن لدولة العنوان. الطريقة المختارة تبقى ما دامت متاحة للعنوان، وإلا الأولى.
  //
  // التسعير يُطلب من عدّة مسارات (تغيّر العنوان أو الطريقة أو السلة أو الكوبون)، وردوده قد تصل بغير ترتيب طلبها.
  // بلا هذا الحارس كان ردّ تسعير قديم قد يصل أخيراً فيكتب مجموعاً وشحناً لا يخصّان الاختيار الحالي — بجوار زرّ إنشاء
  // الطلب. الخادم يُعيد التسعير عند الإنشاء (ADR-0028) فلا يُدفع مبلغ خاطئ، لكن ما يُعرض يجب أن يطابق ما سيُدفع.
  // الأحدث وحده يكتب: ما سُبق يُهمَل ويُعيد null ليعرف مُستدعيه أن نتيجته لم تعد صالحة.
  const quoteSeq = useRef(0);
  const quoteWith = useCallback(async (code, methodId) => {
    const seq = ++quoteSeq.current;
    const quoted = await api.quoteBasket(code, { methodId, country });
    if (seq !== quoteSeq.current) return null;
    setQuote(quoted);
    setShippingMethodId(pickShippingMethod(quoted.shippingMethods?.options, methodId));
    return quoted;
  }, [country]);

  // الشحن (المرحلة 12): يُعاد التسعير بتغيّر العنوان (دولته) أو الطريقة أو السلة.
  useEffect(() => {
    if (savedAddresses === null || order) return;
    quoteWith(appliedCode.current, shippingMethodId).catch(() => {});
  }, [savedAddresses, shippingMethodId, basket.subtotal, basket.itemCount, quoteWith, order]);

  // الكوبون: مرفوضه نتيجةٌ في التسعير لا خطأ — تُعرض رسالته في مكانه، ويُعاد التسعير بلا الكوبون.
  const requote = useCallback(async (code) => {
    setCouponBusy(true); setCouponError(null);
    try {
      const quoted = await quoteWith(code, shippingMethodId);
      if (!quoted) return;   // سبقه تسعير أحدث: حالة الكوبون تتبع الأحدث لا هذا الردّ
      const problem = couponProblemMessage(quoted.coupon, {
        translate: (c) => (i18n.exists(`errors.codes.${c}`) ? t(`errors.codes.${c}`) : null),
        preferServerDetail: (i18n.language || 'ar').startsWith('ar'),
      });
      appliedCode.current = problem ? null : quoted.coupon.code;
      setCouponPreview(problem ? null : { code: quoted.coupon.code, discountAmount: quoted.discount });
      setCouponError(problem);
      if (problem) await quoteWith(null, shippingMethodId);
    } catch (err) { setCouponError(err.message); setCouponPreview(null); }
    finally { setCouponBusy(false); }
  }, [t, i18n, quoteWith, shippingMethodId]);

  const applyCoupon = () => requote(couponCode.trim());

  // تغيّرت السلة بعد تطبيق الكوبون (درج السلة متاح هنا أيضاً) ⇒ إعادة التحقّق من الكوبون نفسه.
  // المرجع يُحدَّث في تأثير لا في جسم العرض: كتابة ref أثناء العرض تكسر ضمانات React
  // (وتُعطّل العرض المتزامن)، والغرض هنا واحد — قراءة أحدث نسخة من requote بلا إعادة تشغيل
  // التأثير التالي كلّما تغيّرت هويتها.
  const requoteRef = useRef(requote);
  useEffect(() => { requoteRef.current = requote; }, [requote]);
  useEffect(() => {
    if (appliedCode.current) requoteRef.current(appliedCode.current);
  }, [basket.subtotal, basket.itemCount]);

  const createOrder = async (e) => {
    e.preventDefault();
    setAddressTouched(true);
    if (addressError || blocked || shippingIssue) return;

    setBusy(true); setServerError(null);
    try {
      const created = await api.createOrder({
        // بلا أسطر: الخادم يُنشئ الطلب من السلة نفسها ويسعّرها بالخطّ نفسه (المرحلة 9).
        ...shippingPayload(shippingChoice, address),
        couponCode: couponPreview?.code ?? null,
        shippingMethodId: shipping?.options?.length ? shippingMethodId : null,
      });
      setOrder(created);
      refreshProducts?.(); // المخزون تغيّر (حُجز) على الخادم
    } catch (err) { setServerError(err.message); }
    finally { setBusy(false); }
  };

  // الخادم استهلك المشترى من السلة عند تأكيد الدفع (المرحلة 9) — نعيد قراءتها بدل تفريغها محلياً.
  const onPaid = () => {
    reload();
    // رقم الطلب في الرابط أيضاً: تحديث صفحة التأكيد بعد الدفع يجب ألّا يمحو التأكيد (المرحلة 16).
    navigate(`/confirmation?order=${order.orderId}`, {
      replace: true,
      state: { order: { orderId: order.orderId, orderNumber: order.orderNumber, total: order.totalAmount, currency: order.currency } },
    });
  };

  if (!loaded && !order) {
    return <div className="souq-layout"><Skeleton height={320} radius={14} /></div>;
  }

  // قراءةٌ فاشلة ليست سلّةً فارغة: «أضف منتجات أولاً» لمشترٍ أصنافه على الخادم تدفعه
  // لإعادة الشراء أو للانصراف. تُفحص الحالة قبل الفراغ، ومعها إعادة محاولة. (CartContext)
  if (loadFailed && items.length === 0 && !order) {
    return (
      <div className="souq-layout">
        <ErrorBanner message={t('cart.loadFailed')} onRetry={reload} />
      </div>
    );
  }

  if (items.length === 0 && !order) {
    return (
      <div className="souq-layout">
        <EmptyState icon={PackageIcon} title={t('checkout.emptyCartTitle')} message={t('checkout.emptyCartMessage')}
          actionLabel={t('checkout.browseStore')} onAction={() => navigate('/')} />
      </div>
    );
  }

  const discountAmount = order?.discountAmount ?? couponPreview?.discountAmount ?? 0;
  const shippingCost = order?.shippingCost ?? quote?.shipping ?? 0;
  const grandTotal = order?.totalAmount ?? quote?.total ?? total;
  // الضريبة كما سعّرها الخادم — لا حساب هنا (ADR-0055). الطلبُ المُنشأ لا يعيدها في عقده، فتُقرأ
  // من عرض السعر الحيّ أو من السلّة، وتظهر في الملخّص حين تُجمَع وحدها.
  const taxAmount = quote?.tax ?? basket.tax ?? 0;

  return (
    <div className={`souq-layout ${styles.grid}`}>
      <OrderSummaryPanel items={items} subtotal={basket.subtotal} discountAmount={discountAmount} shipping={shippingCost}
        shippingPending={!order && shippingIssue === 'required'} tax={taxAmount} total={grandTotal} currency={currency} />
      <div>
        {serverError && <ErrorBanner message={serverError} />}
        {!order ? (
          <AddressStep
            savedAddresses={savedAddresses} shippingChoice={shippingChoice} setShippingChoice={setShippingChoice}
            address={address} setAddress={setAddress}
            addressTouched={addressTouched} setAddressTouched={setAddressTouched} addressError={addressError}
            shipping={shipping} shippingMethodId={shippingMethodId} setShippingMethodId={setShippingMethodId}
            shippingIssue={shippingIssue} currency={currency}
            couponCode={couponCode} setCouponCode={setCouponCode}
            couponPreview={couponPreview} couponError={couponError} couponBusy={couponBusy} onApplyCoupon={applyCoupon}
            busy={busy} blocked={blocked} onSubmit={createOrder}
          />
        ) : (
          <>
            {/* الطلب أُنشئ وحُجز مخزونه ولم يُدفع بعد. قول ذلك صراحةً أصدق من خطوة دفع بلا رجعة
                ظاهرة: المشتري الذي أخطأ عنوانه يعرف أن له مخرجاً، ومن أين. */}
            <p className={styles.placedNote}>{t('checkout.orderPlacedNote', { number: order.orderNumber })}</p>
            <CardPaymentForm order={order} onPaid={onPaid} />
          </>
        )}
      </div>
    </div>
  );
}
