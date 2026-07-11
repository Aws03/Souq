import { useCart } from '../context/CartContext';
import { emojiFor } from '../utils/productEmoji';

// درج السلة: يعرض محتواها ويتيح تعديل الكميات والانتقال للدفع.
export default function CartDrawer({ open, onClose, onCheckout }) {
  const { items, total, inc, dec, remove } = useCart();
  if (!open) return null;

  return (
    <>
      <div className="drawer-overlay" onClick={onClose} />
      <aside className="drawer">
        <div className="drawer-head">
          <h3>سلّتك</h3>
          <button onClick={onClose} style={{ background: 'none', color: '#fff', fontSize: 22 }}>✕</button>
        </div>
        <div className="drawer-body">
          {items.length === 0 ? (
            <div className="empty">سلّتك فارغة — ابدأ التسوّق!</div>
          ) : (
            items.map((i) => (
              <div className="cart-line" key={i.id}>
                <span className="emoji">{emojiFor(i)}</span>
                <div style={{ flex: 1 }}>
                  <div style={{ fontWeight: 700 }}>{i.name}</div>
                  <div style={{ color: 'var(--muted)', fontSize: 13 }}>
                    {i.price.toFixed(2)} {i.currency}
                  </div>
                </div>
                <div className="qty">
                  <button onClick={() => dec(i.id)}>−</button>
                  <span>{i.qty}</span>
                  <button onClick={() => inc(i.id)}>+</button>
                </div>
                <button onClick={() => remove(i.id)} style={{ background: 'none', color: 'var(--clay)' }}>🗑</button>
              </div>
            ))
          )}
        </div>
        {items.length > 0 && (
          <div className="drawer-foot">
            <div className="total-row"><span>الإجمالي</span><span>{total.toFixed(2)} JOD</span></div>
            <button className="checkout-btn" onClick={onCheckout}>متابعة الدفع</button>
          </div>
        )}
      </aside>
    </>
  );
}
