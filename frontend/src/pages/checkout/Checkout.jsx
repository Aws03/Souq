import { useEffect, useState } from 'react';
import { useNavigate, useOutletContext } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useCart } from '../../context/CartContext';
import { api } from '../../api/client';
import {
  NEW_ADDRESS, initialShippingChoice, isShippingChoiceMissing, shippingPayload,
} from '../../features/checkout/shippingChoice';
import { ErrorBanner, EmptyState } from '../../components/common/StateViews';
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
  const { t } = useTranslation();
  const { items, total, clear } = useCart();
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

  const currency = items[0]?.currency || 'JOD';
  const addressError = isShippingChoiceMissing(shippingChoice, address) ? t('checkout.addressRequired') : null;

  // دفتر العناوين (المرحلة 7): الافتراضي للشحن مختار مبدئياً. تعذّر تحميله ⇒ عنوان نصّي كما قبل.
  useEffect(() => {
    api.getMyAddresses()
      .then((list) => { setSavedAddresses(list); setShippingChoice(initialShippingChoice(list)); })
      .catch(() => setSavedAddresses([]));
  }, []);

  const applyCoupon = async () => {
    setCouponBusy(true); setCouponError(null);
    try {
      const preview = await api.applyCoupon(couponCode.trim(), total);
      setCouponPreview(preview);
    } catch (err) { setCouponError(err.message); setCouponPreview(null); }
    finally { setCouponBusy(false); }
  };

  const createOrder = async (e) => {
    e.preventDefault();
    setAddressTouched(true);
    if (addressError) return;

    setBusy(true); setServerError(null);
    try {
      const created = await api.createOrder({
        ...shippingPayload(shippingChoice, address),
        items: items.map((i) => ({ productId: i.id, quantity: i.qty })),
        couponCode: couponPreview?.code ?? null,
      });
      setOrder(created);
      refreshProducts?.(); // المخزون تغيّر (حُجز) على الخادم
    } catch (err) { setServerError(err.message); }
    finally { setBusy(false); }
  };

  const onPaid = () => {
    clear();
    navigate('/confirmation', {
      replace: true,
      state: { order: { orderId: order.orderId, total: order.totalAmount, currency: order.currency } },
    });
  };

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
            busy={busy} onSubmit={createOrder}
          />
        ) : (
          <CardPaymentForm order={order} onPaid={onPaid} />
        )}
      </div>
    </div>
  );
}
