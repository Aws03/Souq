import { NavLink } from 'react-router-dom';
import { GridIcon, PackageIcon, TagIcon, ReceiptIcon, PercentIcon } from '../../components/icons/Icons';
import styles from './AdminLayout.module.css';

const NAV = [
  { to: '/admin', end: true, label: 'لوحة التحكم', icon: GridIcon },
  { to: '/admin/products', label: 'المنتجات', icon: PackageIcon },
  { to: '/admin/categories', label: 'الفئات', icon: TagIcon },
  { to: '/admin/coupons', label: 'الكوبونات', icon: PercentIcon },
  { to: '/admin/orders', label: 'الطلبات', icon: ReceiptIcon },
];

// الشريط الجانبي للوحة الإدارة (سطح المكتب) — يختفي على الجوال لصالح شريط سفلي.
export default function AdminSidebar({ onLogout }) {
  const linkClass = ({ isActive }) => `${styles.navLink} ${isActive ? styles.active : ''}`;

  return (
    <aside className={styles.sidebar}>
      <div className={styles.brand}>Mar<span>ka</span> · الإدارة</div>
      <nav className={styles.nav}>
        {NAV.map(({ to, end, label, icon: Icon }) => (
          <NavLink key={to} to={to} end={end} className={linkClass}>
            <Icon size={17} /> {label}
          </NavLink>
        ))}
      </nav>
      <div className={styles.sidebarFoot}>
        <a href="/" className={styles.navLink}>← المتجر</a>
        <button className={styles.logout} onClick={onLogout}>تسجيل الخروج</button>
      </div>
    </aside>
  );
}
