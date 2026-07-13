import { NavLink } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { GridIcon, PackageIcon, InventoryIcon, TagIcon, ReceiptIcon, PercentIcon } from '../../components/icons/Icons';
import styles from './AdminLayout.module.css';

// شريط تبويب سفلي يستبدل الشريط الجانبي على شاشات الجوال في لوحة الإدارة.
export default function AdminMobileTabBar() {
  const { t } = useTranslation();
  const tabClass = ({ isActive }) => `${styles.tab} ${isActive ? styles.tabActive : ''}`;

  const TABS = [
    { to: '/admin', end: true, label: t('admin.nav.home'), icon: GridIcon },
    { to: '/admin/products', label: t('admin.nav.products'), icon: PackageIcon },
    { to: '/admin/inventory', label: t('admin.nav.inventory'), icon: InventoryIcon },
    { to: '/admin/categories', label: t('admin.nav.categories'), icon: TagIcon },
    { to: '/admin/coupons', label: t('admin.nav.coupons'), icon: PercentIcon },
    { to: '/admin/orders', label: t('admin.nav.orders'), icon: ReceiptIcon },
  ];

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
