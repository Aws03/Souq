import Button from '../common/Button';
import styles from './Hero.module.css';

// بانر كامل العرض: عنوان قوي، سطر فرعي، وزر دعوة واحد يقود لشبكة المنتجات.
export default function Hero({ targetId = 'product-grid' }) {
  const scrollToGrid = () => {
    document.getElementById(targetId)?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  };

  return (
    <section className={styles.hero}>
      <div className={styles.inner}>
        <h1 className={styles.headline}>تسوّق بثقة، <b>بهويّة عربية أصيلة</b></h1>
        <p className={styles.subline}>منتجات منتقاة بعناية — من الإلكترونيات إلى صناعات يدوية أصيلة، توصيل سريع وضمان جودة.</p>
        <Button variant="saffron" size="lg" onClick={scrollToGrid}>تسوّق الآن</Button>
      </div>
    </section>
  );
}
