import { useMemo } from 'react';
import { api } from '../../api/client';
import { queryKeys } from '../../app/queryKeys';
import StoreSettingsEditor from '../../components/settings/StoreSettingsEditor';

// ============================================================================
// محرّر إعدادات المتجر نفسه، من المنصّة: مصدره نقاط /api/platform/tenants/{id}، وخياراته من خيارات التجهيز
// (نفس كائن خيارات المحرّر — اختبار تكامل يقارن الحمولتين). لا قواعد هنا ولا نموذج ثانٍ.
// ============================================================================
export default function PlatformStoreSettings({ store, onSaved, actions, onDirtyChange }) {
  const source = useMemo(() => ({
    settingsKey: queryKeys.platformStoreSettings(store.id),
    loadSettings: () => api.getPlatformStore(store.id).then((detail) => detail.settings),
    optionsKey: [...queryKeys.provisioningOptions(), 'settings'],
    loadOptions: () => api.getProvisioningOptions().then((options) => options.settings),
    save: (payload) => api.updatePlatformStoreSettings(store.id, payload),
    upload: (asset, file) => api.uploadPlatformStoreBranding(store.id, asset, file),
    onSaved,
    fallbackName: store.name,
    currencyHint: 'platform.profile.currencyHint',
  }), [store.id, store.name, onSaved]);

  return <StoreSettingsEditor source={source} actions={actions} onDirtyChange={onDirtyChange} />;
}
