import { useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { api } from '../../api/client';
import { queryKeys } from '../../app/queryKeys';
import { useTenant } from '../../app/TenantProvider';
import StoreSettingsEditor from '../../components/settings/StoreSettingsEditor';
import adminStyles from './Admin.module.css';

// إعدادات المتجر من داخله (store.settings.manage): المتجر من المضيف، فلا معرّف في أيّ طلب. المحرّر نفسه
// مشترك مع المنصّة؛ هنا مصدره فقط — نقاط /api/admin/store، وبعد الحفظ تلبس اللوحة الهوية الجديدة فوراً.
export default function StoreSettings() {
  const { t } = useTranslation();
  const tenant = useTenant();

  const source = useMemo(() => ({
    settingsKey: queryKeys.storeSettings(),
    loadSettings: api.getStoreSettings,
    optionsKey: queryKeys.storeSettingsOptions(),
    loadOptions: api.getStoreSettingsOptions,
    save: api.updateStoreSettings,
    upload: api.uploadStoreBranding,
    onSaved: tenant.refresh,
    fallbackName: tenant.config?.name ?? '',
  }), [tenant.refresh, tenant.config?.name]);

  return (
    <div>
      <h2 className={adminStyles.pageTitle}>{t('admin.settings.title')}</h2>
      <p className={adminStyles.pageSub}>{t('admin.settings.subtitle')}</p>
      <StoreSettingsEditor source={source} />
    </div>
  );
}
