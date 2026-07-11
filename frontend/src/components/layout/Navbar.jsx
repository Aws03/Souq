import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useCart } from '../../context/CartContext';
import { useAuth } from '../../context/AuthContext';
import { setLanguage } from '../../i18n';
import { CartIcon, MenuIcon } from '../icons/Icons';
import SearchBar from './SearchBar';
import MobileMenu from './MobileMenu';
import styles from './Navbar.module.css';

// شريط تنقّل المتجر: ثابت أعلى الصفحة، الشعار يميناً (RTL)، البحث وسطاً،
// السلة والمصادقة يساراً. يتحوّل على الجوال إلى هامبرغر + ورقة سفلية.
export default function Navbar({ onCartClick, searchTerm, onSearchChange }) {
  const { t, i18n } = useTranslation();
  const { count } = useCart();
  const { user, isAuthenticated, isAdmin, logout } = useAuth();
  const [menuOpen, setMenuOpen] = useState(false);

  const otherLanguage = i18n.language === 'ar' ? 'en' : 'ar';
  const toggleLanguage = () => setLanguage(otherLanguage);

  return (
    <nav className={styles.navbar}>
      <button type="button" className={styles.hamburger} onClick={() => setMenuOpen(true)} aria-label={t('nav.openMenu')}>
        <MenuIcon />
      </button>

      <Link to="/" className={styles.brand}>Mar<span>ka</span></Link>

      <SearchBar value={searchTerm} onChange={onSearchChange} className={styles.searchDesktop} />

      <div className={styles.actions}>
        {isAdmin && <Link to="/admin" className={styles.linkLight}>{t('nav.adminPanel')}</Link>}

        {isAuthenticated ? (
          <>
            <span className={styles.user}>{t('nav.hello', { name: user.fullName?.split(' ')[0] })}</span>
            <button className={styles.linkBtn} onClick={logout}>{t('nav.logout')}</button>
          </>
        ) : (
          <>
            <Link to="/login" className={styles.linkLight}>{t('nav.login')}</Link>
            <Link to="/register" className={styles.linkLight}>{t('nav.register')}</Link>
          </>
        )}

        <button type="button" className={styles.langToggle} onClick={toggleLanguage} aria-label={t('nav.langToggleAria')}>
          {otherLanguage === 'en' ? 'EN' : 'ع'}
        </button>

        <button className={styles.cartBtn} onClick={onCartClick} aria-label={t('nav.viewCart')}>
          <CartIcon size={18} />
          {count > 0 && <span className={styles.cartCount}>{count}</span>}
        </button>
      </div>

      <MobileMenu open={menuOpen} onClose={() => setMenuOpen(false)}
        searchTerm={searchTerm} onSearchChange={onSearchChange} />
    </nav>
  );
}
