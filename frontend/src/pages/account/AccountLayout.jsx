import { NavLink, Outlet } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useModule } from '../../app/TenantProvider';
import { HeartIcon, MapPinIcon, ReceiptIcon, UserIcon } from '../../components/icons/Icons';
import styles from './AccountLayout.module.css';

// ============================================================================
// قشرة حساب العميل — كانت مؤجَّلة إلى المرحلة 16 في FrontendArchitecture.md §2.
//
// قبلها كانت صفحات العميل جزراً منفصلة: "حسابي" صفحة واحدة تحمل الملف والعناوين والخصوصية معاً،
// و"طلباتي" مسار لا رابط بينه وبينها إلّا قائمة المستخدم في شريط التنقّل. هنا منطقة واحدة بتنقّل واحد.
//
// المسارات القائمة (/orders و/orders/:id) لم تتغيّر: روابط البريد والإشعارات تشير إليها،
// وقشرة جديدة لا يصحّ أن تكسر رابطاً أُرسل بالأمس. الجديد وحده هو /account/addresses.
//
// عنوان المستند تضبطه كل صفحة داخلية بنفسها (usePageMetadata)، لا القشرة: تأثيرات React تعمل من
// الابن إلى الأب، فعنوان تضبطه القشرة يمحو عنوان الصفحة لا العكس.
//
// رابط المفضّلة يظهر بشرط الوحدة (كما في شريط التنقّل) — والشرط تجربة لا حماية: الخادم يرفض
// نقاط الوحدة المعطّلة بنفسه، وحارس المسار RequireModule باقٍ في App.jsx.
// ============================================================================
export default function AccountLayout() {
  const { t } = useTranslation();
  const wishlistEnabled = useModule('wishlist');

  const linkClass = ({ isActive }) => `${styles.link} ${isActive ? styles.active : ''}`;

  return (
    <div className={`souq-layout ${styles.wrap}`}>
      <header className={styles.head}>
        <h1 className={styles.title}>{t('account.title')}</h1>
        <p className={styles.subtitle}>{t('account.subtitle')}</p>
      </header>

      <div className={styles.body}>
        <nav className={styles.nav} aria-label={t('account.navAria')}>
          <NavLink to="/account" end className={linkClass}>
            <UserIcon size={16} /> {t('account.profileTitle')}
          </NavLink>
          <NavLink to="/account/addresses" className={linkClass}>
            <MapPinIcon size={16} /> {t('account.addressesTitle')}
          </NavLink>
          <NavLink to="/orders" end className={linkClass}>
            <ReceiptIcon size={16} /> {t('orders.myOrdersTitle')}
          </NavLink>
          {wishlistEnabled && (
            <NavLink to="/wishlist" className={linkClass}>
              <HeartIcon size={16} /> {t('wishlist.title')}
            </NavLink>
          )}
        </nav>

        <div className={styles.panel}>
          <Outlet />
        </div>
      </div>
    </div>
  );
}
