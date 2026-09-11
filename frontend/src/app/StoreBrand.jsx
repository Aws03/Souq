import { useTranslation } from 'react-i18next';
import { useStoreConfig } from './TenantProvider';
import { storeName } from './tenantModel';
import styles from './StoreBrand.module.css';

// اسم المتجر أو شعاره حيث كان اسم علامة مكتوباً في الواجهة (المرحلة 15، A4) — من إعداده، بلغة الزائر. على مضيف المنصّة (بلا متجر)
// اسم المنصّة.
export default function StoreBrand({ className = '' }) {
  const { t } = useTranslation();
  const name = useStoreName() || t('platform.name');
  const logo = useStoreConfig()?.settings?.branding?.logoUrl;
  return logo
    ? <img src={logo} alt={name} className={`${styles.logo} ${className}`} />
    : <span className={className}>{name}</span>;
}

export function useStoreName() {
  const { i18n } = useTranslation();
  return storeName(useStoreConfig(), i18n.language);
}
