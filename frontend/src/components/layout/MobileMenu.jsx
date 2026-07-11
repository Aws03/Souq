import { Link } from 'react-router-dom';
import { useAuth } from '../../context/AuthContext';
import SearchBar from './SearchBar';
import styles from './MobileMenu.module.css';

// ورقة سفلية (Bottom Sheet) تظهر على الجوال عند فتح زر الهامبرغر: بحث + روابط.
export default function MobileMenu({ open, onClose, searchTerm, onSearchChange }) {
  const { user, isAuthenticated, isAdmin, logout } = useAuth();
  if (!open) return null;

  return (
    <>
      <div className={styles.overlay} onClick={onClose} />
      <div className={styles.sheet} role="dialog" aria-modal="true">
        <SearchBar value={searchTerm} onChange={onSearchChange} className={styles.search} />
        <nav className={styles.links} onClick={onClose}>
          {isAdmin && <Link to="/admin">لوحة الإدارة</Link>}
          {isAuthenticated ? (
            <>
              <span className={styles.hello}>مرحباً، {user.fullName?.split(' ')[0]}</span>
              <button type="button" onClick={logout}>تسجيل الخروج</button>
            </>
          ) : (
            <>
              <Link to="/login">دخول</Link>
              <Link to="/register">تسجيل حساب</Link>
            </>
          )}
        </nav>
      </div>
    </>
  );
}
