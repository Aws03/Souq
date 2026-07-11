import { useState } from 'react';
import { useCart } from '../context/CartContext';
import { api } from '../api/client';

// صفحة الدفع: تجمع العنوان ورمز الدفع، وتُرسل الطلب للخادم الحقيقي.
export default function Checkout({ onPlaced, onBack }) {
  const { items, total, clear } = useCart();
  const [address, setAddress] = useState('');
  const [token, setToken] = useState('card-ok');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);

  const placeOrder = async () => {
    setBusy(true); setError(null);
    try {
      // TODO (المرحلة 2): يُستبدل customerId الثابت بهوية المستخدم من توكن JWT.
      const created = await api.createOrder({
        customerId: 1,
        shippingAddress: address,
        items: items.map((i) => ({ productId: i.id, quantity: i.qty })),
        paymentToken: token,
      });
      clear();
      // نعرض ما أكّده الخادم (المصدر الوحيد للحقيقة)، لا حسابات الواجهة.
      onPlaced({ orderId: created.orderId, total: created.totalAmount, currency: created.currency });
    } catch (e) { setError(e.message); }
    finally { setBusy(false); }
  };

  return (
    <div className="layout">
      <div className="panel">
        <h2 style={{ marginBottom: 20 }}>إتمام الطلب</h2>
        <div className="field">
          <label>عنوان الشحن</label>
          <textarea rows={3} value={address} onChange={(e) => setAddress(e.target.value)}
            placeholder="المدينة، الحي، الشارع…" />
        </div>
        <div className="field">
          <label>رمز الدفع (جرّب "fail" لمحاكاة الرفض)</label>
          <input value={token} onChange={(e) => setToken(e.target.value)} />
        </div>
        {error && <div style={{ color: 'var(--clay)', marginBottom: 12 }}>⚠ {error}</div>}
        <div className="total-row"><span>الإجمالي</span><span>{total.toFixed(2)} JOD</span></div>
        <button className="checkout-btn" disabled={busy || !address || items.length === 0} onClick={placeOrder}>
          {busy ? 'جارٍ المعالجة…' : 'ادفع الآن'}
        </button>
        <button onClick={onBack} style={{ background: 'none', color: 'var(--muted)', marginTop: 14, width: '100%' }}>
          ← العودة للمتجر
        </button>
      </div>
    </div>
  );
}
