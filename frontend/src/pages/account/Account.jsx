import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api } from '../../api/client';
import { useAuth } from '../../context/AuthContext';
import { useToast } from '../../context/ToastContext';
import Skeleton from '../../components/common/Skeleton';
import { ErrorBanner } from '../../components/common/StateViews';
import ProfileForm from './ProfileForm';
import AddressBook from './AddressBook';
import PrivacyPanel from './PrivacyPanel';
import styles from './Account.module.css';

// صفحة "حسابي" (المرحلة 7): البيانات الشخصية ودفتر العناوين والخصوصية. كل ما هنا للعميل الحالي فقط — لا معرّف عميل
// يُرسَل؛ الخادم يستخرجه من الجلسة، وحساب موظّف يُرفض بـ 403 CustomerAccountRequired.
export default function Account() {
  const { t } = useTranslation();
  const toast = useToast();
  const { reloadUser } = useAuth();
  const [profile, setProfile] = useState(null);
  const [error, setError] = useState(null);

  const load = useCallback(
    () => api.getMyProfile().then((p) => { setProfile(p); setError(null); }).catch((e) => setError(e.message)),
    []);

  useEffect(() => { load(); }, [load]);

  const saveProfile = async (payload) => {
    try {
      setProfile(await api.updateMyProfile(payload));
      toast.success(t('account.profileSaved'));
      reloadUser().catch(() => {}); // الاسم في شريط التنقّل
    } catch (e) { toast.error(e.message); }
  };

  return (
    <div className="souq-layout">
      <h1 className={styles.title}>{t('account.title')}</h1>
      <p className={styles.subtitle}>{t('account.subtitle')}</p>

      {error && <ErrorBanner message={error} />}

      {!error && profile === null && (
        <div className={styles.grid}>
          <Skeleton height={280} radius={14} />
          <Skeleton height={280} radius={14} />
        </div>
      )}

      {profile && (
        <>
          {profile.status === 'Blocked' && <div className={styles.notice}>{t('account.blockedNotice')}</div>}
          <div className={styles.grid}>
            <div className={styles.stack}>
              <ProfileForm key={profile.id} profile={profile} onSave={saveProfile} />
              <PrivacyPanel />
            </div>
            <AddressBook addresses={profile.addresses} onChanged={load} />
          </div>
        </>
      )}
    </div>
  );
}
