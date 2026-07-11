import { Outlet, useNavigate } from 'react-router-dom';
import { useAuth } from '../../context/AuthContext';
import AdminSidebar from './AdminSidebar';
import AdminMobileTabBar from './AdminMobileTabBar';
import styles from './AdminLayout.module.css';

// ============================================================================
// تخطيط لوحة الإدارة — منفصل فعلياً عن تخطيط المتجر (شريط جانبي + ترويسة خاصة)،
// لا هو صفحة المتجر بأزرار مخفية. العميل لا يصل هنا إطلاقاً (AdminRoute + الخادم).
// على الجوال يتحوّل الشريط الجانبي إلى شريط تبويب سفلي (AdminMobileTabBar).
// ============================================================================
export default function AdminLayout() {
  const { user, logout } = useAuth();
  const navigate = useNavigate();

  const doLogout = () => { logout(); navigate('/login', { replace: true }); };

  return (
    <div className={styles.shell}>
      <AdminSidebar onLogout={doLogout} />

      <div className={styles.main}>
        <header className={styles.header}>
          <span className={styles.headerTitle}>لوحة تحكّم المتجر</span>
          <span className={styles.headerUser}>{user?.fullName || 'المدير'}</span>
        </header>
        <main className={styles.content}>
          <Outlet />
        </main>
      </div>

      <AdminMobileTabBar />
    </div>
  );
}
