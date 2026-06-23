import { useCart } from '../context/CartContext';

export default function Navbar({ onCartClick }) {
  const { count } = useCart();
  return (
    <nav className="navbar">
      <div className="brand">سو<span>ق</span></div>
      <button className="cart-btn" onClick={onCartClick}>
        🛒 السلة
        {count > 0 && <span className="cart-count">{count}</span>}
      </button>
    </nav>
  );
}
