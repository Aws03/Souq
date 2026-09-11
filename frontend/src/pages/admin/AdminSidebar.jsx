import { NavLink } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../../context/AuthContext';
import { GridIcon, PackageIcon, InventoryIcon, TagIcon, ReceiptIcon, PercentIcon, UserIcon, CardIcon } from '../../components/icons/Icons';
import styles from './AdminLayout.module.css';

// الشريط الجانبي للوحة الإدارة (سطح المكتب) — يختفي على الجوال لصالح شريط سفلي.
// كل رابط بصلاحيته من الخادم: الموظّف لا يرى ما سيرفضه الخادم بـ 403 (الكوبونات مثلاً).
export default function AdminSidebar({ onLogout }) {
  const { t } = useTranslation();
  const { can } = useAuth();
  const linkClass = ({ isActive }) => `${styles.navLink} ${isActive ? styles.active : ''}`;

  const NAV = [
    { to: '/admin', end: true, label: t('admin.nav.dashboard'), icon: GridIcon },
    { to: '/admin/products', label: t('admin.nav.products'), icon: PackageIcon, permission: 'catalog.manage' },
    { to: '/admin/inventory', label: t('admin.nav.inventory'), icon: InventoryIcon, permission: 'inventory.view' },
    { to: '/admin/categories', label: t('admin.nav.categories'), icon: TagIcon, permission: 'catalog.manage' },
    { to: '/admin/coupons', label: t('admin.nav.coupons'), icon: PercentIcon, permission: 'promotions.manage' },
    { to: '/admin/orders', label: t('admin.nav.orders'), icon: ReceiptIcon, permission: 'orders.view' },
    { to: '/admin/customers', label: t('admin.nav.customers'), icon: UserIcon, permission: 'customers.view' },
    { to: '/admin/payments', label: t('admin.nav.payments'), icon: CardIcon, permission: 'store.payments.manage' },
  ].filter((item) => !item.permission || can(item.permission));

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
