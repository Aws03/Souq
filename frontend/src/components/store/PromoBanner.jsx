import styles from './PromoBanner.module.css';
import Button from '../common/Button';

const MEDIA_CLASSES = ['promo__media0', 'promo__media1'];

// شريط ترويجي مقسوم: صورة (تدرّج بديل مؤقتاً) + عنوان ونص فرعي وزر دعوة.
// reverse يعكس ترتيب الجانبين — يُستخدم لتبديل الشريط الثاني بصرياً عن الأول.
export default function PromoBanner({ headline, subline, ctaLabel, onCtaClick, reverse = false, variant = 0 }) {
  return (
    <section className={`${styles.promo} ${reverse ? styles.promo__reverse : ''}`}>
      <div className={`${styles.promo__media} ${styles[MEDIA_CLASSES[variant % MEDIA_CLASSES.length]]}`} aria-hidden="true" />
      <div className={styles.promo__content}>
        <h3 className={styles.promo__headline}>{headline}</h3>
        <p className={styles.promo__subline}>{subline}</p>
        {ctaLabel && <Button variant="primary" size="lg" onClick={onCtaClick}>{ctaLabel}</Button>}
      </div>
    </section>
  );
}
