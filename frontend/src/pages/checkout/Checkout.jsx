import { useState } from 'react';
import { useNavigate, useOutletContext } from 'react-router-dom';
import { useCart } from '../../context/CartContext';
import { api } from '../../api/client';
import { ErrorBanner, EmptyState } from '../../components/common/StateViews';
import { PackageIcon } from '../../components/icons/Icons';
import CheckoutForm from './CheckoutForm';
import OrderSummaryPanel from './OrderSummaryPanel';
import styles from './Checkout.module.css';

const CARD_DIGITS = /^\d{16}$/;
const EXPIRY_PATTERN = /^\d{2}\/\d{2}$/;

// صفحة الدفع (محمية: تتطلّب تسجيل الدخول). عمودان: الملخّص والنموذج.
// رمز الدفع الفعلي يُشتقّ من حقل CVV التجريبي (000 يحاكي رفضاً من بوابة الدفع).
export default function Checkout() {
  const { items, total, clear } = useCart();
  const { refreshProducts } = useOutletContext();
  const navigate = useNavigate();

  const [address, setAddress] = useState('');
  const [card, setCard] = useState({ number: '', expiry: '', cvv: '' });
  const [touched, setTouched] = useState({});
  const [busy, setBusy] = useState(false);
  const [serverError, setServerError] = useState(null);

  const errors = {
    address: !address.trim() ? 'عنوان الشحن مطلوب' : null,
    number: !CARD_DIGITS.test(card.number.replace(/\s/g, '')) ? 'رقم بطاقة غير صالح (16 رقماً)' : null,
    expiry: !EXPIRY_PATTERN.test(card.expiry) ? 'صيغة غير صالحة (MM/YY)' : null,
    cvv: !/^\d{3}$/.test(card.cvv) ? 'CVV مكوّن من 3 أرقام' : null,
  };
  const isValid = !Object.values(errors).some(Boolean);

  const submit = async (e) => {
    e.preventDefault();
    setTouched({ address: true, number: true, expiry: true, cvv: true });
    if (!isValid) return;

    setBusy(true); setServerError(null);
    try {
      const created = await api.createOrder({
        shippingAddress: address,
        items: items.map((i) => ({ productId: i.id, quantity: i.qty })),
        paymentToken: card.cvv === '000' ? 'fail' : 'card-ok',
      });
      clear();
      refreshProducts?.();
      navigate('/confirmation', {
        replace: true,
        state: { order: { orderId: created.orderId, total: created.totalAmount, currency: created.currency } },
      });
    } catch (err) { setServerError(err.message); }
    finally { setBusy(false); }
  };

  if (items.length === 0) {
    return (
      <div className="souq-layout">
        <EmptyState icon={PackageIcon} title="سلّتك فارغة" message="أضِف منتجات أولاً قبل إتمام الطلب."
          actionLabel="تصفّح المتجر" onAction={() => navigate('/')} />
      </div>
    );
  }

  const currency = items[0]?.currency || 'JOD';

  return (
    <div className={`souq-layout ${styles.grid}`}>
      <OrderSummaryPanel items={items} total={total} currency={currency} />
      <div>
        {serverError && <ErrorBanner message={serverError} />}
        <CheckoutForm address={address} setAddress={setAddress} card={card} setCard={setCard}
          errors={errors} touched={touched} setTouched={setTouched} busy={busy} onSubmit={submit} />
      </div>
    </div>
  );
}
