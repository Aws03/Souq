import { useCallback, useEffect, useRef, useState } from 'react';
import { useNavigate, useOutletContext } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useCart } from '../../context/CartContext';
import { api } from '../../api/client';
import {
  NEW_ADDRESS, initialShippingChoice, isShippingChoiceMissing, shippingPayload,
} from '../../features/checkout/shippingChoice';
import { ErrorBanner, EmptyState } from '../../components/common/StateViews';
import Skeleton from '../../components/common/Skeleton';
import { couponProblemMessage, hasProblems } from '../../features/basket/basketModel';
import { PackageIcon } from '../../components/icons/Icons';
import AddressStep from './AddressStep';
import CardPaymentForm from './CardPaymentForm';
import OrderSummaryPanel from './OrderSummaryPanel';
import styles from './Checkout.module.css';

// صفحة الدفع (محمية: تتطلّب تسجيل الدخول) — خطوتان:
//  1) عنوان الشحن + كوبون اختياري → ينشئ الطلب على الخادم (يحجز المخزون
//     وينشئ نيّة دفع لدى Stripe) ويعيد ClientSecret.
//  2) بطاقة حقيقية عبر Stripe Elements (لا تصل تفاصيلها خادمنا إطلاقاً) →
//     تأكيد لدى الخادم يتحقّق من النتيجة مع Stripe نفسها قبل إتمام الطلب.
export default function Checkout() {
  const { t, i18n } = useTranslation();
  const { basket, items, total, loaded, reload } = useCart();
  const { refreshProducts } = useOutletContext();
  const navigate = useNavigate();

  const [savedAddresses, setSavedAddresses] = useState(null); // null حتى يُحمَّل دفتر العناوين
  const [shippingChoice, setShippingChoice] = useState(NEW_ADDRESS);
  const [address, setAddress] = useState('');
  const [addressTouched, setAddressTouched] = useState(false);
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

  // دفتر العناوين (المرحلة 7): الافتراضي للشحن مختار مبدئياً. تعذّر تحميله ⇒ عنوان نصّي كما قبل.
  useEffect(() => {
    api.getMyAddresses()
      .then((list) => { setSavedAddresses(list); setShippingChoice(initialShippingChoice(list)); })
      .catch(() => setSavedAddresses([]));
  }, []);

  // الخصم من الخادم بالخطّ نفسه الذي يُنشئ الطلب (المرحلة 8): ما يُعرض هنا هو ما سيُدفع. كوبون مرفوض نتيجةٌ في السلة
  // لا خطأ — تُعرض رسالته في مكان الكوبون.
  const requote = useCallback(async (code) => {
    setCouponBusy(true); setCouponError(null);
    try {
      const quoted = await api.quoteBasket(code);
      const problem = couponProblemMessage(quoted.coupon, {
        translate: (c) => (i18n.exists(`errors.codes.${c}`) ? t(`errors.codes.${c}`) : null),
        preferServerDetail: (i18n.language || 'ar').startsWith('ar'),
      });
      appliedCode.current = problem ? null : quoted.coupon.code;
      setCouponPreview(problem ? null : { code: quoted.coupon.code, discountAmount: quoted.discount, newTotal: quoted.total });
      setCouponError(problem);
    } catch (err) { setCouponError(err.message); setCouponPreview(null); }
    finally { setCouponBusy(false); }
  }, [t, i18n]);

  const applyCoupon = () => requote(couponCode.trim());

  // تغيّرت السلة بعد تطبيق الكوبون (درج السلة متاح هنا أيضاً) ⇒ إعادة التسعير بالرمز نفسه.
  useEffect(() => {
    if (appliedCode.current) requote(appliedCode.current);
  }, [basket.subtotal, basket.itemCount, requote]);

  const createOrder = async (e) => {
    e.preventDefault();
    setAddressTouched(true);
    if (addressError || blocked) return;

    setBusy(true); setServerError(null);
    try {
      const created = await api.createOrder({
        // بلا أسطر: الخادم يُنشئ الطلب من السلة نفسها ويسعّرها بالخطّ نفسه (المرحلة 9).
        ...shippingPayload(shippingChoice, address),
        couponCode: couponPreview?.code ?? null,
      });
      setOrder(created);
      refreshProducts?.(); // المخزون تغيّر (حُجز) على الخادم
    } catch (err) { setServerError(err.message); }
    finally { setBusy(false); }
  };

  // الخادم استهلك المشترى من السلة عند تأكيد الدفع (المرحلة 9) — نعيد قراءتها بدل تفريغها محلياً.
  const onPaid = () => {
    reload();
    navigate('/confirmation', {
      replace: true,
      state: { order: { orderId: order.orderId, orderNumber: order.orderNumber, total: order.totalAmount, currency: order.currency } },
    });
  };

  if (!loaded && !order) {
    return <div className="souq-layout"><Skeleton height={320} radius={14} /></div>;
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
  const grandTotal = order?.totalAmount ?? couponPreview?.newTotal ?? total;

  return (
    <div className={`souq-layout ${styles.grid}`}>
      <OrderSummaryPanel items={items} subtotal={total} discountAmount={discountAmount} total={grandTotal} currency={currency} />
      <div>
        {serverError && <ErrorBanner message={serverError} />}
        {!order ? (
          <AddressStep
            savedAddresses={savedAddresses} shippingChoice={shippingChoice} setShippingChoice={setShippingChoice}
            address={address} setAddress={setAddress}
            addressTouched={addressTouched} setAddressTouched={setAddressTouched} addressError={addressError}
            couponCode={couponCode} setCouponCode={setCouponCode}
            couponPreview={couponPreview} couponError={couponError} couponBusy={couponBusy} onApplyCoupon={applyCoupon}
            busy={busy} blocked={blocked} onSubmit={createOrder}
          />
        ) : (
          <CardPaymentForm order={order} onPaid={onPaid} />
        )}
      </div>
    </div>
  );
}
