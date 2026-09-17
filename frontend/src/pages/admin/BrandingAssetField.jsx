import { useId, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import Button from '../../components/common/Button';
import styles from './StoreSettings.module.css';

// ============================================================================
// ملف هوية (شعار، أيقونة، صورة مشاركة): يُرفع فور اختياره لا مع "حفظ". الخادم يفحص المحتوى لا الاسم
// ويولّد الرابط تحت بادئة المتجر (ADR-0016) — فلا حقل رابط هنا يُكتب فيه شيء.
//
// accept والحجم تلميحان يوفّران رحلة فاشلة، لا حماية: ملف PNG باسم .svg يمرّ من هنا ويُرفض هناك.
// ============================================================================
const ACCEPT = {
  logo: 'image/png,image/jpeg,image/webp',
  favicon: 'image/png,image/x-icon,image/vnd.microsoft.icon,.ico',
  'social-image': 'image/png,image/jpeg',
};

export default function BrandingAssetField({ asset, url, maxBytes, onUpload }) {
  const { t } = useTranslation();
  const id = useId();
  const input = useRef(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);

  const choose = async (event) => {
    const file = event.target.files?.[0];
    event.target.value = '';
    if (!file) return;
    if (file.size > maxBytes) {
      setError(t('admin.settings.assets.tooLarge', { size: Math.round(maxBytes / 1024 / 1024) }));
      return;
    }
    setBusy(true); setError(null);
    try { await onUpload(asset, file); }
    catch (err) { setError(err.message); }
    finally { setBusy(false); }
  };

  return (
    <div className={styles.asset}>
      <div className={`${styles.assetThumb} ${asset === 'social-image' ? styles.assetWide : ''}`}>
        {url ? <img src={url} alt={t(`admin.settings.assets.${asset}.alt`)} />
          : <span className={styles.assetEmpty}>{t('admin.settings.assets.none')}</span>}
      </div>
      <div className={styles.assetBody}>
        <label className={styles.assetLabel} htmlFor={id}>{t(`admin.settings.assets.${asset}.label`)}</label>
        <span className={styles.hint}>{t(`admin.settings.assets.${asset}.hint`)}</span>
        <input ref={input} id={id} type="file" accept={ACCEPT[asset]} className={styles.fileInput} onChange={choose} />
        <Button variant="ghost" size="sm" type="button" loading={busy} onClick={() => input.current?.click()}>
          {url ? t('admin.settings.assets.replace') : t('admin.settings.assets.upload')}
        </Button>
        {error && <span className={styles.fieldError} role="alert">{error}</span>}
      </div>
    </div>
  );
}
