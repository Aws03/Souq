import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api } from '../../api/client';
import { usePageMetadata } from '../../app/usePageMetadata';
import Skeleton from '../../components/common/Skeleton';
import { ErrorBanner } from '../../components/common/StateViews';
import AddressBook from './AddressBook';

// دفتر العناوين بصفحته (المرحلة 16). يجلب /account/addresses مباشرةً بدل اشتقاقه من الملف الشخصي:
// الصفحة تطلب ما تعرضه وحده، ولا يُعاد تحميل الملف كلّه بعد كل تعديل عنوان.
export default function Addresses() {
  const { t } = useTranslation();
  const [addresses, setAddresses] = useState(null);
  const [error, setError] = useState(null);
  usePageMetadata({ title: t('account.addressesTitle') });

  const load = useCallback(
    () => api.getMyAddresses().then((list) => { setAddresses(list); setError(null); }).catch((e) => setError(e.message)),
    []);

  useEffect(() => { load(); }, [load]);

  if (error) return <ErrorBanner message={error} onRetry={load} />;
  if (!addresses) return <Skeleton height={280} radius={14} />;

  return <AddressBook addresses={addresses} onChanged={load} />;
}
