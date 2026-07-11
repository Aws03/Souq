import { useTranslation } from 'react-i18next';
import Button from '../common/Button';
import styles from './Hero.module.css';

// بانر كامل العرض: عنوان قوي، سطر فرعي، وزر دعوة واحد يقود لشبكة المنتجات.
export default function Hero({ targetId = 'product-grid' }) {
  const { t } = useTranslation();
  const scrollToGrid = () => {
    document.getElementById(targetId)?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  };

  return (
    <section className={styles.hero}>
      <div className={styles.inner}>
        <h1 className={styles.headline}>{t('store.heroHeadlinePrefix')}<b>{t('store.heroHeadlineStrong')}</b></h1>
        <p className={styles.subline}>{t('store.heroSubline')}</p>
        <Button variant="saffron" size="lg" onClick={scrollToGrid}>{t('store.heroCta')}</Button>
      </div>
    </section>
  );
}
