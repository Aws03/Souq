import { NavLink } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useAdminNav } from './AdminSidebar';
import styles from './AdminLayout.module.css';

// شريط تبويب سفلي يستبدل الشريط الجانبي على شاشات الجوال في لوحة الإدارة (بالصلاحيات والوحدات نفسها).
export default function AdminMobileTabBar() {
  const { t } = useTranslation();
  const nav = useAdminNav();
  const tabClass = ({ isActive }) => `${styles.tab} ${isActive ? styles.tabActive : ''}`;

  return (
    <nav className={styles.tabBar}>
      {nav.map(({ to, end, label, shortLabel, icon: Icon }) => (
        <NavLink key={to} to={to} end={end} className={tabClass}>
          <Icon size={19} /><span>{t(shortLabel ?? label)}</span>
        </NavLink>
      ))}
    </nav>
  );
}
