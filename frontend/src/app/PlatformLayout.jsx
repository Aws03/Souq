import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import styles from './PlatformLayout.module.css';

// ============================================================================
// منطقة المنصّة (المرحلة 15، المنطقة الرابعة): تخطيطها وحارسها على مضيف المنصّة — لا على مضيف أي متجر (الخادم يرفض نقاطها هناك
// بـ 404). شاشاتها (المتاجر، التجهيز، الحسابات، سجلّ التدقيق) في المرحلة 18؛ هنا الإطار الذي تُبنى فيه، والدخول والخروج.
// ============================================================================
export default function PlatformLayout() {
  const { t } = useTranslation();
  const { user, logout } = useAuth();
  const navigate = useNavigate();

  const signOut = () => { logout(); navigate('/login', { replace: true }); };

  return (
    <div className={styles.shell}>
      <header className={styles.header}>
        <strong className={styles.brand}>{t('platform.name')}</strong>
        <div className={styles.account}>
          <span>{t('platform.signedInAs', { name: user?.fullName })}</span>
          <button type="button" className={styles.signOut} onClick={signOut}>{t('platform.logout')}</button>
        </div>
      </header>
      <main className={styles.content}>
        <h1 className={styles.title}>{t('platform.title')}</h1>
        <p className={styles.subtitle}>{t('platform.subtitle')}</p>
      </main>
    </div>
  );
}
