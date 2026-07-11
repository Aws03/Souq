import { useTranslation } from 'react-i18next';
import styles from './Spinner.module.css';

// دوّارة تحميل بسيطة (CSS بحت، بلا صور). inverted للاستخدام فوق خلفيات داكنة/زعفرانية.
export default function Spinner({ size = 18, inverted = false }) {
  const { t } = useTranslation();
  return (
    <span
      className={`${styles.spinner} ${inverted ? styles.inverted : ''}`}
      style={{ width: size, height: size }}
      role="status"
      aria-label={t('common.loading')}
    />
  );
}
