import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useCart } from '../../context/CartContext';
import { useWishlist } from '../../context/WishlistContext';
import { useAuth } from '../../context/AuthContext';
import { useModule, useStoreConfig, useTheme } from '../../app/TenantProvider';
import StoreBrand from '../../app/StoreBrand';
import { enabledLanguages } from '../../app/tenantModel';
import { setLanguage } from '../../i18n';
import { CartIcon, HeartIcon, MenuIcon, MoonIcon, SunIcon } from '../icons/Icons';
import NotificationBell from '../notifications/NotificationBell';
import SearchBar from './SearchBar';
import { useStoreSearch } from '../../features/catalog/useStoreSearch';
import MobileMenu from './MobileMenu';
import styles from './Navbar.module.css';

// شريط تنقّل المتجر: خلفية بيضاء ثابتة أعلى الصفحة (تحت شريط الإعلان)، اسم المتجر أو شعاره يميناً (RTL)، البحث وسطاً، أيقونات
// اللغة/المفضّلة/السلة يساراً. يتحوّل على الجوال إلى هامبرغر + ورقة سفلية. المرحلة 15: الهوية من إعداد المتجر (لا سمة يختارها
// الزائر)، وتبديل اللغة والمفضّلة بما يفعّله المتجر.
export default function Navbar({ onCartClick }) {
  const { t, i18n } = useTranslation();
  const { count } = useCart();
  const { count: wishlistCount } = useWishlist();
  const { user, isAuthenticated, canManageStore, logout } = useAuth();
  const [menuOpen, setMenuOpen] = useState(false);
  const wishlistEnabled = useModule('wishlist');
  const languages = enabledLanguages(useStoreConfig());
  const search = useStoreSearch();

  const { theme, toggleTheme } = useTheme();
  const otherLanguage = i18n.language === 'ar' ? 'en' : 'ar';
  const canToggleLanguage = languages.includes(otherLanguage);
  const toggleLanguage = () => setLanguage(otherLanguage);

  return (
    <nav className={styles.navbar}>
      <button type="button" className={styles.hamburger} onClick={() => setMenuOpen(true)} aria-label={t('nav.openMenu')}>
        <MenuIcon />
      </button>

      <Link to="/" className={styles.brand}><StoreBrand /></Link>

      <SearchBar value={search.value} onChange={search.setValue} onSubmit={search.submit}
        className={styles.searchDesktop} />

      <div className={styles.actions}>
        {canManageStore && <Link to="/admin" className={styles.linkLight}>{t('nav.adminPanel')}</Link>}

        {isAuthenticated ? (
          <>
            <Link to="/orders" className={`${styles.linkLight} ${styles.desktopOnly}`}>{t('nav.myOrders')}</Link>
            {/* "حسابي" لحساب عميل فقط (له ملف شراء) — نقاط /api/account ترفض الموظّف بـ 403. */}
            {user.customerId && <Link to="/account" className={`${styles.linkLight} ${styles.desktopOnly}`}>{t('nav.account')}</Link>}
            <span className={styles.user}>{t('nav.hello', { name: user.fullName?.split(' ')[0] })}</span>
            <button className={styles.linkBtn} onClick={logout}>{t('nav.logout')}</button>
          </>
        ) : (
          <>
            <Link to="/login" className={styles.linkLight}>{t('nav.login')}</Link>
            <Link to="/register" className={styles.linkLight}>{t('nav.register')}</Link>
          </>
        )}

        {canToggleLanguage && (
          <button type="button" className={`${styles.langToggle} ${styles.desktopOnly}`} onClick={toggleLanguage} aria-label={t('nav.langToggleAria')}>
            {otherLanguage === 'en' ? 'EN' : 'ع'}
          </button>
        )}

        {/* الأيقونة تعرض الوجهة لا الحالة: في الوضع الداكن يُعرض رمز الشمس لأن الضغط يُنير.
            aria-pressed يقول الحالة الفعلية لقارئ الشاشة، فلا يعتمد المعنى على الرسم. */}
        <button
          type="button"
          className={`${styles.iconBtn} ${styles.desktopOnly}`}
          onClick={toggleTheme}
          aria-pressed={theme === 'dark'}
          aria-label={t('nav.themeToggleAria')}
          title={t(theme === 'dark' ? 'nav.switchToLight' : 'nav.switchToDark')}
        >
          {theme === 'dark' ? <SunIcon size={18} /> : <MoonIcon size={18} />}
        </button>

        {/* إشعارات الحساب (المرحلة 14): حالة الطلبات للعميل — ظاهرة على الجوال أيضاً. */}
        <NotificationBell />

        {wishlistEnabled && (
          <Link to="/wishlist" className={`${styles.iconBtn} ${styles.desktopOnly}`} aria-label={t('nav.wishlistAria')}>
            <HeartIcon size={18} filled={wishlistCount > 0} />
            {wishlistCount > 0 && <span className={styles.iconBadge}>{wishlistCount}</span>}
          </Link>
        )}

        {/* السلة تبقى ظاهرة على الجوال أيضاً — إجراء أساسي في متجر إلكتروني،
            بخلاف اللغة/المفضّلة اللتين تنتقلان إلى القائمة السفلية هناك. */}
        <button className={styles.iconBtn} onClick={onCartClick} aria-label={t('nav.viewCart')}>
          <CartIcon size={18} />
          {count > 0 && <span className={styles.iconBadge}>{count}</span>}
        </button>
      </div>

      <MobileMenu open={menuOpen} onClose={() => setMenuOpen(false)}
        search={search}
        currentLanguage={i18n.language} otherLanguage={otherLanguage}
        onToggleLanguage={canToggleLanguage ? toggleLanguage : undefined} />
    </nav>
  );
}
