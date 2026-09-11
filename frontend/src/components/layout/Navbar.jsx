import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useCart } from '../../context/CartContext';
import { useWishlist } from '../../context/WishlistContext';
import { useAuth } from '../../context/AuthContext';
import { setLanguage } from '../../i18n';
import { getTheme, setTheme } from '../../theme';
import { CartIcon, HeartIcon, MenuIcon } from '../icons/Icons';
import SearchBar from './SearchBar';
import MobileMenu from './MobileMenu';
import ThemeSwitcher from './ThemeSwitcher';
import styles from './Navbar.module.css';

// شريط تنقّل المتجر: خلفية بيضاء ثابتة أعلى الصفحة (تحت شريط الإعلان)، الشعار
// يميناً (RTL)، البحث وسطاً، أيقونات اللغة/المفضّلة/السلة يساراً. يتحوّل على
// الجوال إلى هامبرغر + ورقة سفلية.
export default function Navbar({ onCartClick, searchTerm, onSearchChange }) {
  const { t, i18n } = useTranslation();
  const { count } = useCart();
  const { count: wishlistCount } = useWishlist();
  const { user, isAuthenticated, canManageStore, logout } = useAuth();
  const [menuOpen, setMenuOpen] = useState(false);
  const [theme, setThemeState] = useState(getTheme());

  const otherLanguage = i18n.language === 'ar' ? 'en' : 'ar';
  const toggleLanguage = () => setLanguage(otherLanguage);

  const changeTheme = (next) => { setTheme(next); setThemeState(next); };

  return (
    <nav className={styles.navbar}>
      <button type="button" className={styles.hamburger} onClick={() => setMenuOpen(true)} aria-label={t('nav.openMenu')}>
        <MenuIcon />
      </button>

      <Link to="/" className={styles.brand}>Mar<span>ka</span></Link>

      <SearchBar value={searchTerm} onChange={onSearchChange} className={styles.searchDesktop} />

      <div className={styles.actions}>
        {canManageStore && <Link to="/admin" className={styles.linkLight}>{t('nav.adminPanel')}</Link>}

        {isAuthenticated ? (
          <>
            <Link to="/orders" className={`${styles.linkLight} ${styles.desktopOnly}`}>{t('nav.myOrders')}</Link>
            <span className={styles.user}>{t('nav.hello', { name: user.fullName?.split(' ')[0] })}</span>
            <button className={styles.linkBtn} onClick={logout}>{t('nav.logout')}</button>
          </>
        ) : (
          <>
            <Link to="/login" className={styles.linkLight}>{t('nav.login')}</Link>
            <Link to="/register" className={styles.linkLight}>{t('nav.register')}</Link>
          </>
        )}

        <button type="button" className={`${styles.langToggle} ${styles.desktopOnly}`} onClick={toggleLanguage} aria-label={t('nav.langToggleAria')}>
          {otherLanguage === 'en' ? 'EN' : 'ع'}
        </button>

        <div className={styles.desktopOnly}>
          <ThemeSwitcher current={theme} onChange={changeTheme} />
        </div>

        <Link to="/wishlist" className={`${styles.iconBtn} ${styles.desktopOnly}`} aria-label={t('nav.wishlistAria')}>
          <HeartIcon size={18} filled={wishlistCount > 0} />
          {wishlistCount > 0 && <span className={styles.iconBadge}>{wishlistCount}</span>}
        </Link>

        {/* السلة تبقى ظاهرة على الجوال أيضاً — إجراء أساسي في متجر إلكتروني،
            بخلاف اللغة/المفضّلة اللتين تنتقلان إلى القائمة السفلية هناك. */}
        <button className={styles.iconBtn} onClick={onCartClick} aria-label={t('nav.viewCart')}>
          <CartIcon size={18} />
          {count > 0 && <span className={styles.iconBadge}>{count}</span>}
        </button>
      </div>

      <MobileMenu open={menuOpen} onClose={() => setMenuOpen(false)}
        searchTerm={searchTerm} onSearchChange={onSearchChange}
        currentLanguage={i18n.language} otherLanguage={otherLanguage} onToggleLanguage={toggleLanguage}
        theme={theme} onThemeChange={changeTheme} />
    </nav>
  );
}
