import { useTranslation } from 'react-i18next';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../../api/client';
import { queryKeys } from '../../app/queryKeys';
import { usePageMetadata } from '../../app/usePageMetadata';
import Skeleton from '../../components/common/Skeleton';
import { ErrorBanner } from '../../components/common/StateViews';
import AddressBook from './AddressBook';

// دفتر العناوين بصفحته (المرحلة 16). يجلب /account/addresses مباشرةً بدل اشتقاقه من الملف الشخصي:
// الصفحة تطلب ما تعرضه وحده، ولا يُعاد تحميل الملف كلّه بعد كل تعديل عنوان.
export default function Addresses() {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  usePageMetadata({ title: t('account.addressesTitle') });

  const { data: addresses, error, refetch, isPending } = useQuery({
    queryKey: queryKeys.myAddresses(),
    queryFn: api.getMyAddresses,
  });

  // الخادم ينقل صفة "الافتراضي" عند حذف عنوان افتراضي، فلا تُحسب هنا — يُعاد الجلب.
  // والملف الشخصي يحمل نسخته من العناوين أيضاً، فيُبطَل معها.
  const reload = () => Promise.all([
    queryClient.invalidateQueries({ queryKey: queryKeys.myAddresses() }),
    queryClient.invalidateQueries({ queryKey: queryKeys.myProfile() }),
  ]);

  if (error) return <ErrorBanner message={error.message} onRetry={refetch} />;
  if (isPending || !addresses) return <Skeleton height={280} radius={14} />;

  return <AddressBook addresses={addresses} onChanged={reload} />;
}
