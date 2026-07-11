import { useTranslation } from 'react-i18next';
import styles from './AnnouncementBar.module.css';

// شريط إعلان رفيع فوق شريط التنقّل — رسالة واحدة تتكرّر بتمرير أفقي مستمر
// (Marquee) عبر CSS خالص، بلا JavaScript متحرّك (أخف وأصدق مع تفضيل تقليل الحركة).
export default function AnnouncementBar() {
  const { t } = useTranslation();
  const message = t('announcement.freeShipping');

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
