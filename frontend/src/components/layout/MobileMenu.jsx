import { Link } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../../context/AuthContext';
import { useModule } from '../../app/TenantProvider';
import { useDialog } from '../common/useDialog';
import SearchBar from './SearchBar';
import styles from './MobileMenu.module.css';

// ورقة سفلية (Bottom Sheet) تظهر على الجوال عند فتح زر الهامبرغر: بحث + روابط + المفضّلة وتبديل اللغة (انتقلا هنا من شريط التنقّل
// على الجوال لتفادي الازدحام). المرحلة 15: لا سمة يختارها الزائر (الهوية قرار المتجر)، والمفضّلة وتبديل اللغة بما يفعّله المتجر.
export default function MobileMenu({
  open, onClose, search, currentLanguage, otherLanguage, onToggleLanguage,
}) {
  const { t } = useTranslation();
  const { user, isAuthenticated, canManageStore, logout } = useAuth();
  const wishlist = useModule('wishlist');
  const sheetRef = useDialog(open, onClose);
  if (!open) return null;

  return (
    <>
      <button type="button" className={styles.overlay} aria-label={t('common.close')} onClick={onClose} />
      <div ref={sheetRef} tabIndex={-1} className={styles.sheet} role="dialog" aria-modal="true">
        <SearchBar value={search.value} onChange={search.setValue}
          onSubmit={() => { search.submit(); onClose(); }} className={styles.search} />
        <nav className={styles.links}>
          {wishlist && <Link to="/wishlist" onClick={onClose}>{t('nav.wishlistAria')}</Link>}
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
          {onToggleLanguage && (
            <button type="button" onClick={onToggleLanguage} aria-label={t('nav.langToggleAria')}>
              {currentLanguage === 'ar' ? 'English' : 'العربية'} ({otherLanguage === 'en' ? 'EN' : 'ع'})
            </button>
          )}
        </nav>
      </div>
    </>
  );
}
