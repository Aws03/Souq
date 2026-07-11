import { Link } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../../context/AuthContext';
import SearchBar from './SearchBar';
import styles from './MobileMenu.module.css';

// ورقة سفلية (Bottom Sheet) تظهر على الجوال عند فتح زر الهامبرغر: بحث + روابط.
export default function MobileMenu({ open, onClose, searchTerm, onSearchChange }) {
  const { t } = useTranslation();
  const { user, isAuthenticated, isAdmin, logout } = useAuth();
  if (!open) return null;

  return (
    <>
      <div className={styles.overlay} onClick={onClose} />
      <div className={styles.sheet} role="dialog" aria-modal="true">
        <SearchBar value={searchTerm} onChange={onSearchChange} className={styles.search} />
        <nav className={styles.links} onClick={onClose}>
          {isAdmin && <Link to="/admin">{t('nav.adminPanel')}</Link>}
          {isAuthenticated ? (
            <>
              <span className={styles.hello}>{t('nav.hello', { name: user.fullName?.split(' ')[0] })}</span>
              <button type="button" onClick={logout}>{t('nav.logoutFull')}</button>
            </>
          ) : (
            <>
              <Link to="/login">{t('nav.login')}</Link>
              <Link to="/register">{t('nav.registerFull')}</Link>
            </>
          )}
        </nav>
      </div>
    </>
  );
}
