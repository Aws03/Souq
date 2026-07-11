import { NavLink } from 'react-router-dom';
import { GridIcon, PackageIcon, TagIcon, ReceiptIcon, PercentIcon } from '../../components/icons/Icons';
import styles from './AdminLayout.module.css';

const TABS = [
  { to: '/admin', end: true, label: 'الرئيسية', icon: GridIcon },
  { to: '/admin/products', label: 'المنتجات', icon: PackageIcon },
  { to: '/admin/categories', label: 'الفئات', icon: TagIcon },
  { to: '/admin/coupons', label: 'الكوبونات', icon: PercentIcon },
  { to: '/admin/orders', label: 'الطلبات', icon: ReceiptIcon },
];

// شريط تبويب سفلي يستبدل الشريط الجانبي على شاشات الجوال في لوحة الإدارة.
export default function AdminMobileTabBar() {
  const tabClass = ({ isActive }) => `${styles.tab} ${isActive ? styles.tabActive : ''}`;

  return (
    <nav className={styles.tabBar}>
      {TABS.map(({ to, end, label, icon: Icon }) => (
        <NavLink key={to} to={to} end={end} className={tabClass}>
          <Icon size={19} /><span>{label}</span>
        </NavLink>
      ))}
    </nav>
  );
}
