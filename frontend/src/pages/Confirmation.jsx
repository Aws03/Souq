// شاشة تأكيد الطلب — "الفراغ والنجاح لحظات للتوجيه لا للمزاج".
export default function Confirmation({ order, onContinue }) {
  return (
    <div className="layout">
      <div className="panel" style={{ textAlign: 'center' }}>
        <div className="success-icon">✓</div>
        <h2>تم استلام طلبك</h2>
        <p style={{ color: 'var(--muted)', margin: '8px 0 20px' }}>
          رقم الطلب <b style={{ color: 'var(--petrol)' }}>#{order.orderId}</b> — سنُرسل لك تأكيداً بالبريد.
        </p>
        <div className="total-row"><span>المدفوع</span><span>{order.total.toFixed(2)} JOD</span></div>
        <button className="checkout-btn" onClick={onContinue}>متابعة التسوّق</button>
      </div>
    </div>
  );
}
