import { NavLink, Outlet, useNavigate } from 'react-router-dom';
import { Suspense } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import { setLanguage } from '../i18n';
import { useTheme } from './TenantProvider';
import { PagePending } from '../components/ProtectedRoute';
import { GridIcon, MoonIcon, PackageIcon, SunIcon } from '../components/icons/Icons';
import styles from '../pages/platform/Platform.module.css';

// ============================================================================
// منطقة المنصّة (المنطقة الرابعة): تخطيطها على مضيفها وحده — لا على مضيف أيّ متجر (الخادم يرفض نقاطها هناك
// بـ 404، ويرفض توكن المتجر هنا بـ 401). شاشاتها: نظرة المنصّة (platform.reports.view) والمتاجر وتجهيزها
// (platform.tenants.manage). الروابط تتبع صلاحيات الحساب كما يمنحها الخادم؛ الحارس عرضٌ لا حماية.
// ============================================================================
export const PLATFORM_NAV = [
  { to: '/platform', end: true, label: 'platform.nav.overview', icon: GridIcon, permission: 'platform.reports.view' },
  { to: '/platform/stores', label: 'platform.nav.stores', icon: PackageIcon, permission: 'platform.tenants.manage' },
];

export default function PlatformLayout() {
  const { t, i18n } = useTranslation();
  const { user, logout, can } = useAuth();
  const { theme, toggleTheme } = useTheme();
  const otherLanguage = i18n.language === 'ar' ? 'en' : 'ar';
  const navigate = useNavigate();

  const signOut = () => { logout(); navigate('/login', { replace: true }); };
  const nav = PLATFORM_NAV.filter((item) => can(item.permission));
  const linkClass = ({ isActive }) => `${styles.navLink} ${isActive ? styles.navActive : ''}`;

  return (
    <div className={styles.shell}>
      <a href="#platform-main" className={styles.skip}>{t('platform.skipToContent')}</a>
      <header className={styles.header}>
        <strong className={styles.brand}>{t('platform.name')}</strong>
        <nav className={styles.nav} aria-label={t('platform.nav.label')}>
          {nav.map(({ to, end, label, icon: Icon }) => (
            <NavLink key={to} to={to} end={end} className={linkClass}><Icon size={16} /> {t(label)}</NavLink>
          ))}
        </nav>
        <div className={styles.account}>
          {/* المنصّة بلا متجر يحدّد لغاته: اللغتان المدعومتان متاحتان دائماً، والوضع كما عند الزوّار. */}
          <button type="button" className={styles.headerButton} onClick={() => setLanguage(otherLanguage)}
            aria-label={t('nav.langToggleAria')}>
            {otherLanguage === 'en' ? 'EN' : 'ع'}
          </button>
          <button type="button" className={styles.headerButton} onClick={toggleTheme} aria-pressed={theme === 'dark'}
            aria-label={t('nav.themeToggleAria')} title={t(theme === 'dark' ? 'nav.switchToLight' : 'nav.switchToDark')}>
            {theme === 'dark' ? <SunIcon size={17} /> : <MoonIcon size={17} />}
          </button>
          <span className={styles.accountName}>{t('platform.signedInAs', { name: user?.fullName })}</span>
          <button type="button" className={styles.signOut} onClick={signOut}>{t('platform.logout')}</button>
        </div>
      </header>

      <main id="platform-main" className={styles.content} tabIndex={-1}>
        <Suspense fallback={<PagePending />}>
          <Outlet />
        </Suspense>
      </main>
    </div>
  );
}
