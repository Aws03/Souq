import { NavLink } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { GridIcon, PackageIcon, TagIcon, ReceiptIcon, PercentIcon } from '../../components/icons/Icons';
import styles from './AdminLayout.module.css';

// الشريط الجانبي للوحة الإدارة (سطح المكتب) — يختفي على الجوال لصالح شريط سفلي.
export default function AdminSidebar({ onLogout }) {
  const { t } = useTranslation();
  const linkClass = ({ isActive }) => `${styles.navLink} ${isActive ? styles.active : ''}`;

  const NAV = [
    { to: '/admin', end: true, label: t('admin.nav.dashboard'), icon: GridIcon },
    { to: '/admin/products', label: t('admin.nav.products'), icon: PackageIcon },
    { to: '/admin/categories', label: t('admin.nav.categories'), icon: TagIcon },
    { to: '/admin/coupons', label: t('admin.nav.coupons'), icon: PercentIcon },
    { to: '/admin/orders', label: t('admin.nav.orders'), icon: ReceiptIcon },
  ];

  return (
    <aside className={styles.sidebar}>
      <div className={styles.brand}>Mar<span>ka</span> · {t('admin.sidebar.title')}</div>
      <nav className={styles.nav}>
        {NAV.map(({ to, end, label, icon: Icon }) => (
          <NavLink key={to} to={to} end={end} className={linkClass}>
            <Icon size={17} /> {label}
          </NavLink>
        ))}
      </nav>
      <div className={styles.sidebarFoot}>
        <a href="/" className={styles.navLink}>{t('admin.backToStore')}</a>
        <button className={styles.logout} onClick={onLogout}>{t('admin.logout')}</button>
      </div>
    </aside>
  );
}
