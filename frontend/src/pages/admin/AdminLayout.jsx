import { Outlet, useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../../context/AuthContext';
import NotificationBell from '../../components/notifications/NotificationBell';
import AdminSidebar from './AdminSidebar';
import AdminMobileTabBar from './AdminMobileTabBar';
import styles from './AdminLayout.module.css';

// ============================================================================
// تخطيط لوحة الإدارة — منفصل فعلياً عن تخطيط المتجر (شريط جانبي + ترويسة خاصة)،
// لا هو صفحة المتجر بأزرار مخفية. العميل لا يصل هنا إطلاقاً (AdminRoute + الخادم).
// على الجوال يتحوّل الشريط الجانبي إلى شريط تبويب سفلي (AdminMobileTabBar).
// ============================================================================
export default function AdminLayout() {
  const { t } = useTranslation();
  const { user, logout } = useAuth();
  const navigate = useNavigate();

  const doLogout = () => { logout(); navigate('/login', { replace: true }); };

  return (
    <div className={styles.shell}>
      <AdminSidebar onLogout={doLogout} />

      <div className={styles.main}>
        <header className={styles.header}>
          <span className={styles.headerTitle}>{t('admin.headerTitle')}</span>
          <div className={styles.headerRight}>
            {/* طلب جديد مدفوع، ومخزون نزل عن حدّه (المرحلة 14) — لكل من يملك صلاحية رؤيتهما. */}
            <NotificationBell />
            <span className={styles.headerUser}>{user?.fullName || t('admin.adminFallback')}</span>
            {/* الرجوع للمتجر + الخروج متاحان على الجوال هنا (الشريط الجانبي يوفّرهما
                على سطح المكتب، لكنه مخفيّ على الجوال لصالح شريط التبويب السفلي). */}
            <a href="/" className={styles.headerAction}>{t('admin.backToStore')}</a>
            <button type="button" className={styles.headerAction} onClick={doLogout}>{t('admin.logout')}</button>
          </div>
        </header>
        <main className={styles.content}>
          <Outlet />
        </main>
      </div>

      <AdminMobileTabBar />
    </div>
  );
}
