import { useCallback } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../../api/client';
import { queryKeys } from '../../app/queryKeys';

// خيارات التجهيز ثابتة ما دام الخادم نفسه — تُقرأ مرّة للجلسة.
export const useProvisioningOptions = () => useQuery({
  queryKey: queryKeys.provisioningOptions(),
  queryFn: api.getProvisioningOptions,
  staleTime: Infinity,
});

// ============================================================================
// متجر واحد من منظور المنصّة: تفاصيله وحسابات إدارته، وتحديث كليهما بعد أيّ تغيير.
//
// الحسابات صفحة واحدة بالحدّ الأقصى (100)، الأحدث أولاً: تكفي لقراءة "هل له مدير؟" في كل متجر واقعي. القائمة
// العامّة تحمل العدد الدقيق من الخادم (ActiveAdmins) ولا تعتمد على هذا.
//
// refresh يُبطل المتجر بكل ما تحته (التفاصيل، الحسابات، الإعدادات) وقائمة المتاجر — فحالته في القائمة لا تتأخّر
// عن حالته هنا. إعدادات المحرّر تُعاد قراءتها ولا تمسّ مسودّته (StoreSettingsEditor).
// ============================================================================
export function usePlatformStore(id) {
  const queryClient = useQueryClient();
  const store = useQuery({ queryKey: queryKeys.platformStore(id), queryFn: () => api.getPlatformStore(id) });
  const accounts = useQuery({
    queryKey: queryKeys.platformStoreAccounts(id),
    queryFn: () => api.getPlatformStoreAccounts(id, { pageSize: 100 }),
  });

  const refresh = useCallback(() => Promise.all([
    queryClient.invalidateQueries({ queryKey: queryKeys.platformStore(id) }),
    queryClient.invalidateQueries({ queryKey: ['platform-stores'] }),
  ]), [queryClient, id]);

  return { store, accounts, refresh };
}
