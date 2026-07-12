import { useState, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import Button from '../common/Button';
import styles from './Hero.module.css';

const SLIDE_INTERVAL_MS = 4000;
// تدرّجات بديلة بديلاً عن صور حقيقية (لا صور بانر متوفّرة بعد) — كل شريحة
// بلونين من نظام التصميم نفسه، لا ألوان جديدة دخيلة.
const SLIDE_BACKGROUND_CLASSES = ['hero__bg0', 'hero__bg1', 'hero__bg2'];

// بانر رئيسي بعرض كامل بعدّة شرائح تتقدّم تلقائياً كل 4 ثوانٍ، مع نقاط تحكّم
// يدوية أسفلها. يتوقّف التقدّم التلقائي إن طلب المستخدم تقليل الحركة.
export default function Hero({ targetId = 'catalog' }) {
  const { t } = useTranslation();
  const slides = t('store.heroSlides', { returnObjects: true });
  const [active, setActive] = useState(0);

  useEffect(() => {
    if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) return;
    const timer = setInterval(() => setActive((i) => (i + 1) % slides.length), SLIDE_INTERVAL_MS);
    return () => clearInterval(timer);
  }, [slides.length]);

  const scrollToGrid = () => {
    document.getElementById(targetId)?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  };

  return (
    <section className={styles.hero} aria-roledescription="carousel">
      {slides.map((slide, i) => (
        <div
          key={i}
          className={[
            styles.hero__slide,
            styles[SLIDE_BACKGROUND_CLASSES[i % SLIDE_BACKGROUND_CLASSES.length]],
            i === active ? styles.hero__slideActive : '',
          ].join(' ')}
          aria-hidden={i !== active}
        >
          <div className={styles.hero__overlay} />
          <div className={styles.hero__inner}>
            <h1 className={styles.hero__headline}>{slide.headline}</h1>
            <p className={styles.hero__subline}>{slide.subline}</p>
            <Button variant="saffron" size="lg" onClick={scrollToGrid}>{slide.cta}</Button>
          </div>
        </div>
      ))}

      <div className={styles.hero__dots} role="tablist" aria-label={t('store.heroDotsAria')}>
        {slides.map((_, i) => (
          <button
            key={i}
            type="button"
            role="tab"
            aria-selected={i === active}
            aria-label={t('store.heroSlideAria', { n: i + 1 })}
            className={`${styles.hero__dot} ${i === active ? styles.hero__dotActive : ''}`}
            onClick={() => setActive(i)}
          />
        ))}
      </div>
    </section>
  );
}
