import { Navigate, useLocation, useNavigate } from 'react-router-dom';

// شاشة تأكيد الطلب — تقرأ الطلب من حالة التوجيه. الوصول المباشر بلا طلب
// (مثل تحديث الصفحة) يعيد للمتجر بدل عرض شاشة فارغة.
export default function Confirmation() {
  const location = useLocation();
  const navigate = useNavigate();
  const order = location.state?.order;

  if (!order) return <Navigate to="/" replace />;

  return (
    <div className="layout">
      <div className="panel" style={{ textAlign: 'center' }}>
        <div className="success-icon">✓</div>
        <h2>تم استلام طلبك</h2>
        <p style={{ color: 'var(--muted)', margin: '8px 0 20px' }}>
          رقم الطلب <b style={{ color: 'var(--petrol)' }}>#{order.orderId}</b> — سنُرسل لك تأكيداً بالبريد.
        </p>
        <div className="total-row"><span>المدفوع</span><span>{order.total.toFixed(2)} {order.currency}</span></div>
        <button className="checkout-btn" onClick={() => navigate('/')}>متابعة التسوّق</button>
      </div>
    </div>
  );
}
