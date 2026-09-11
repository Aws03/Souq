import { Link } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../../context/AuthContext';
import SearchBar from './SearchBar';
import ThemeSwitcher from './ThemeSwitcher';
import styles from './MobileMenu.module.css';

// ورقة سفلية (Bottom Sheet) تظهر على الجوال عند فتح زر الهامبرغر: بحث + روابط
// + المفضّلة وتبديل اللغة (انتقلا هنا من شريط التنقّل على الجوال لتفادي الازدحام).
export default function MobileMenu({
  open, onClose, searchTerm, onSearchChange,
  currentLanguage, otherLanguage, onToggleLanguage, theme, onThemeChange,
}) {
  const { t } = useTranslation();
  const { user, isAuthenticated, canManageStore, logout } = useAuth();
  if (!open) return null;

  return (
    <>
      <div className={styles.overlay} onClick={onClose} />
      <div className={styles.sheet} role="dialog" aria-modal="true">
        <SearchBar value={searchTerm} onChange={onSearchChange} className={styles.search} />
        <nav className={styles.links}>
          <Link to="/wishlist" onClick={onClose}>{t('nav.wishlistAria')}</Link>
          {canManageStore && <Link to="/admin" onClick={onClose}>{t('nav.adminPanel')}</Link>}
          {isAuthenticated ? (
            <>
              <Link to="/orders" onClick={onClose}>{t('nav.myOrders')}</Link>
              {user.customerId && <Link to="/account" onClick={onClose}>{t('nav.account')}</Link>}
              <span className={styles.hello}>{t('nav.hello', { name: user.fullName?.split(' ')[0] })}</span>
              <button type="button" onClick={() => { logout(); onClose(); }}>{t('nav.logoutFull')}</button>
            </>
          ) : (
            <>
              <Link to="/login" onClick={onClose}>{t('nav.login')}</Link>
              <Link to="/register" onClick={onClose}>{t('nav.registerFull')}</Link>
            </>
          )}
          <button type="button" onClick={onToggleLanguage} aria-label={t('nav.langToggleAria')}>
            {currentLanguage === 'ar' ? 'English' : 'العربية'} ({otherLanguage === 'en' ? 'EN' : 'ع'})
          </button>
        </nav>

        <div className={styles.themeRow}>
          <span className={styles.themeLabel}>{t('nav.themeSwitcherAria')}</span>
          <ThemeSwitcher current={theme} onChange={onThemeChange} />
        </div>
      </div>
    </>
  );
}
