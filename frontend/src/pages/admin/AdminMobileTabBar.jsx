import { NavLink } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../../context/AuthContext';
import { GridIcon, PackageIcon, InventoryIcon, TagIcon, ReceiptIcon, PercentIcon, UserIcon, CardIcon } from '../../components/icons/Icons';
import styles from './AdminLayout.module.css';

// شريط تبويب سفلي يستبدل الشريط الجانبي على شاشات الجوال في لوحة الإدارة (بالصلاحيات نفسها).
export default function AdminMobileTabBar() {
  const { t } = useTranslation();
  const { can } = useAuth();
  const tabClass = ({ isActive }) => `${styles.tab} ${isActive ? styles.tabActive : ''}`;

  const TABS = [
    { to: '/admin', end: true, label: t('admin.nav.home'), icon: GridIcon },
    { to: '/admin/products', label: t('admin.nav.products'), icon: PackageIcon, permission: 'catalog.manage' },
    { to: '/admin/inventory', label: t('admin.nav.inventory'), icon: InventoryIcon, permission: 'inventory.view' },
    { to: '/admin/categories', label: t('admin.nav.categories'), icon: TagIcon, permission: 'catalog.manage' },
    { to: '/admin/coupons', label: t('admin.nav.coupons'), icon: PercentIcon, permission: 'promotions.manage' },
    { to: '/admin/orders', label: t('admin.nav.orders'), icon: ReceiptIcon, permission: 'orders.view' },
    { to: '/admin/customers', label: t('admin.nav.customers'), icon: UserIcon, permission: 'customers.view' },
    { to: '/admin/payments', label: t('admin.nav.payments'), icon: CardIcon, permission: 'store.payments.manage' },
  ].filter((tab) => !tab.permission || can(tab.permission));

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
