import { useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { useStoreConfig } from '../../app/TenantProvider';
import { announcementText } from '../../app/tenantModel';
import styles from './AnnouncementBar.module.css';

// شريط إعلان رفيع فوق شريط التنقّل — نصّ المتجر من إعداده بلغة الزائر (المرحلة 15)، يتكرّر بتمرير أفقي مستمر (Marquee) عبر CSS
// خالص. متجر بلا إعلان ⇒ لا شريط، ويُصفَّر ارتفاعه كي لا تترك العناصر الملتصقة فراغاً مكانه.
export default function AnnouncementBar() {
  const { i18n } = useTranslation();
  const message = announcementText(useStoreConfig(), i18n.language);

  useEffect(() => {
    document.documentElement.style.setProperty('--announcement-height', message ? '36px' : '0px');
  }, [message]);

  if (!message) return null;

  return (
    <div className={styles.announcement} role="note">
      <div className={styles.announcement__track}>
        {Array.from({ length: 4 }).map((_, i) => (
          <span key={i} className={styles.announcement__item} aria-hidden={i > 0 ? 'true' : undefined}>
            {message}
          </span>
        ))}
      </div>
    </div>
  );
}
