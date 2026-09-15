import { useTranslation } from 'react-i18next';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../../api/client';
import { queryKeys } from '../../app/queryKeys';
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
  const queryClient = useQueryClient();
  usePageMetadata({ title: t('account.profileTitle') });

  const { data: profile, error, refetch, isPending } = useQuery({
    queryKey: queryKeys.myProfile(),
    queryFn: api.getMyProfile,
  });

  // الحفظ يكتب ردّ الخادم في الذاكرة المؤقّتة مباشرةً بدل جلبٍ ثانٍ: الردّ هو الملف بعد الحفظ.
  const save = useMutation({
    mutationFn: api.updateMyProfile,
    onSuccess: (updated) => {
      queryClient.setQueryData(queryKeys.myProfile(), updated);
      toast.success(t('account.profileSaved'));
      reloadUser().catch(() => {}); // الاسم في شريط التنقّل
    },
    onError: (e) => toast.error(e.message),
  });

  if (error) return <ErrorBanner message={error.message} onRetry={refetch} />;
  if (isPending || !profile) return <Skeleton height={280} radius={14} />;

  return (
    <div className={styles.stack}>
      {/* حساب موقوف: يرى بياناته ويُنزّلها لكنه لا يطلب ولا يقيّم (CustomerStatus.Blocked). */}
      {profile.status === 'Blocked' && <div className={styles.notice}>{t('account.blockedNotice')}</div>}
      <ProfileForm key={profile.id} profile={profile} onSave={save.mutateAsync} />
      <PrivacyPanel />
    </div>
  );
}
