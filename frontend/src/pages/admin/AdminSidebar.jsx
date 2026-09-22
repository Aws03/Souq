import { NavLink } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../../context/AuthContext';
import { useStoreConfig } from '../../app/TenantProvider';
import StoreBrand from '../../app/StoreBrand';
import { isModuleEnabled } from '../../app/tenantModel';
import { CardIcon, GridIcon, InventoryIcon, PackageIcon, PercentIcon, ReceiptIcon, SearchIcon, SlidersIcon, StarIcon, TagIcon, TeamIcon, TrendIcon, TruckIcon, UserIcon } from '../../components/icons/Icons';
import styles from './AdminLayout.module.css';

// روابط لوحة الإدارة بصلاحياتها ووحداتها — مشتركة بين الشريط الجانبي (سطح المكتب) والشريط السفلي (الجوال).
export const ADMIN_NAV = [
  { to: '/admin', end: true, label: 'admin.nav.dashboard', shortLabel: 'admin.nav.home', icon: GridIcon },
  { to: '/admin/business', label: 'admin.nav.business', icon: TrendIcon, permission: 'store.reports.view' },
  { to: '/admin/products', label: 'admin.nav.products', icon: PackageIcon, permission: 'catalog.manage' },
  { to: '/admin/inventory', label: 'admin.nav.inventory', icon: InventoryIcon, permission: 'inventory.view' },
  { to: '/admin/categories', label: 'admin.nav.categories', icon: TagIcon, permission: 'catalog.manage' },
  { to: '/admin/search-synonyms', label: 'admin.nav.searchSynonyms', icon: SearchIcon, permission: 'catalog.manage' },
  { to: '/admin/coupons', label: 'admin.nav.coupons', icon: PercentIcon, permission: 'promotions.manage', module: 'promotions' },
  { to: '/admin/orders', label: 'admin.nav.orders', icon: ReceiptIcon, permission: 'orders.view' },
  { to: '/admin/customers', label: 'admin.nav.customers', icon: UserIcon, permission: 'customers.view' },
  { to: '/admin/reviews', label: 'admin.nav.reviews', icon: StarIcon, permission: 'reviews.moderate', module: 'reviews' },
  { to: '/admin/shipping', label: 'admin.nav.shipping', icon: TruckIcon, permission: 'store.shipping.manage' },
  { to: '/admin/payments', label: 'admin.nav.payments', icon: CardIcon, permission: 'store.payments.manage' },
  { to: '/admin/staff', label: 'admin.nav.staff', icon: TeamIcon, permission: 'store.staff.manage' },
  { to: '/admin/settings', label: 'admin.nav.settings', icon: SlidersIcon, permission: 'store.settings.manage' },
  // اشتراك المتجر وفواتيره (C5): بصلاحية الإعدادات نفسها — شأنُ صاحب المتجر لا موظّفه.
  { to: '/admin/subscription', label: 'admin.nav.subscription', icon: CardIcon, permission: 'store.settings.manage' },
  { to: '/admin/tax', label: 'admin.nav.tax', icon: PercentIcon, permission: 'store.settings.manage' },
];

// الروابط التي يراها الحساب الحالي: صلاحيته من الخادم، والوحدة (إن كانت اختيارية) مفعّلة في متجره — الموظّف لا يرى ما سيرفضه
// الخادم بـ 403، ولا أحد يرى وحدة يرفضها بـ 404 ModuleDisabled.
export function useAdminNav() {
  const { can } = useAuth();
  const config = useStoreConfig();
  return ADMIN_NAV.filter((item) => (!item.permission || can(item.permission))
    && (!item.module || isModuleEnabled(config, item.module)));
}

// الشريط الجانبي للوحة الإدارة (سطح المكتب) — يختفي على الجوال لصالح شريط سفلي.
export default function AdminSidebar({ onLogout }) {
  const { t } = useTranslation();
  const nav = useAdminNav();
  const linkClass = ({ isActive }) => `${styles.navLink} ${isActive ? styles.active : ''}`;

  return (
    <aside className={styles.sidebar}>
      <div className={styles.brand}><StoreBrand /> · {t('admin.sidebar.title')}</div>
      <nav className={styles.nav}>
        {nav.map(({ to, end, label, icon: Icon }) => (
          <NavLink key={to} to={to} end={end} className={linkClass}>
            <Icon size={17} /> {t(label)}
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
