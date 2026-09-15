import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api } from '../../api/client';
import { useAuth } from '../../context/AuthContext';
import { useToast } from '../../context/ToastContext';
import { usePageMetadata } from '../../app/usePageMetadata';
import Skeleton from '../../components/common/Skeleton';
import { ErrorBanner } from '../../components/common/StateViews';
import ProfileForm from './ProfileForm';
import PrivacyPanel from './PrivacyPanel';
import styles from './Account.module.css';

// بيانات العميل الشخصية وخصوصيته، داخل قشرة الحساب (AccountLayout). العناوين انتقلت إلى صفحتها
// في المرحلة 16 — صفحة واحدة كانت تحمل ثلاثة اهتمامات.
//
// كل ما هنا للعميل الحالي فقط: لا معرّف عميل يُرسَل؛ الخادم يستخرجه من الجلسة، وحساب موظّف
// يُرفض بـ 403 CustomerAccountRequired.
export default function Profile() {
  const { t } = useTranslation();
  const toast = useToast();
  const { reloadUser } = useAuth();
  const [profile, setProfile] = useState(null);
  const [error, setError] = useState(null);
  usePageMetadata({ title: t('account.profileTitle') });

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

  if (error) return <ErrorBanner message={error} onRetry={load} />;
  if (!profile) return <Skeleton height={280} radius={14} />;

  return (
    <div className={styles.stack}>
      {/* حساب موقوف: يرى بياناته ويُنزّلها لكنه لا يطلب ولا يقيّم (CustomerStatus.Blocked). */}
      {profile.status === 'Blocked' && <div className={styles.notice}>{t('account.blockedNotice')}</div>}
      <ProfileForm key={profile.id} profile={profile} onSave={saveProfile} />
      <PrivacyPanel />
    </div>
  );
}
