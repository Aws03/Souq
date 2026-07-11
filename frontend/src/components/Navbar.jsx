import { Link } from 'react-router-dom';
import { useCart } from '../context/CartContext';
import { useAuth } from '../context/AuthContext';

// شريط تنقّل المتجر (العميل/الزائر). يعكس حالة المصادقة: زائر يرى دخول/تسجيل،
// مستخدم مسجّل يرى اسمه وخروج، والأدمن يرى رابطاً للوحة الإدارة.
export default function Navbar({ onCartClick }) {
  const { count } = useCart();
  const { user, isAuthenticated, isAdmin, logout } = useAuth();

  return (
    <nav className="navbar">
      <Link to="/" className="brand">سو<span>ق</span></Link>

      <div className="nav-actions">
        {isAdmin && <Link to="/admin" className="nav-link-light">لوحة الإدارة</Link>}

        {isAuthenticated ? (
          <>
            <span className="nav-user">مرحباً، {user.fullName?.split(' ')[0]}</span>
            <button className="nav-link-btn" onClick={logout}>خروج</button>
          </>
        ) : (
          <>
            <Link to="/login" className="nav-link-light">دخول</Link>
            <Link to="/register" className="nav-link-light">تسجيل</Link>
          </>
        )}

        <button className="cart-btn" onClick={onCartClick}>
          🛒 السلة
          {count > 0 && <span className="cart-count">{count}</span>}
        </button>
      </div>
    </nav>
  );
}
