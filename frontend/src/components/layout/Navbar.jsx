import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useCart } from '../../context/CartContext';
import { useAuth } from '../../context/AuthContext';
import { CartIcon, MenuIcon } from '../icons/Icons';
import SearchBar from './SearchBar';
import MobileMenu from './MobileMenu';
import styles from './Navbar.module.css';

// شريط تنقّل المتجر: ثابت أعلى الصفحة، الشعار يميناً (RTL)، البحث وسطاً،
// السلة والمصادقة يساراً. يتحوّل على الجوال إلى هامبرغر + ورقة سفلية.
export default function Navbar({ onCartClick, searchTerm, onSearchChange }) {
  const { count } = useCart();
  const { user, isAuthenticated, isAdmin, logout } = useAuth();
  const [menuOpen, setMenuOpen] = useState(false);

  return (
    <nav className={styles.navbar}>
      <button type="button" className={styles.hamburger} onClick={() => setMenuOpen(true)} aria-label="فتح القائمة">
        <MenuIcon />
      </button>

      <Link to="/" className={styles.brand}>Mar<span>ka</span></Link>

      <SearchBar value={searchTerm} onChange={onSearchChange} className={styles.searchDesktop} />

      <div className={styles.actions}>
        {isAdmin && <Link to="/admin" className={styles.linkLight}>لوحة الإدارة</Link>}

        {isAuthenticated ? (
          <>
            <span className={styles.user}>مرحباً، {user.fullName?.split(' ')[0]}</span>
            <button className={styles.linkBtn} onClick={logout}>خروج</button>
          </>
        ) : (
          <>
            <Link to="/login" className={styles.linkLight}>دخول</Link>
            <Link to="/register" className={styles.linkLight}>تسجيل</Link>
          </>
        )}

        <button className={styles.cartBtn} onClick={onCartClick} aria-label="عرض السلة">
          <CartIcon size={18} />
          {count > 0 && <span className={styles.cartCount}>{count}</span>}
        </button>
      </div>

      <MobileMenu open={menuOpen} onClose={() => setMenuOpen(false)}
        searchTerm={searchTerm} onSearchChange={onSearchChange} />
    </nav>
  );
}
