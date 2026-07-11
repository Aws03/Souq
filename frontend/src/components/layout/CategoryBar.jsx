import { useTranslation } from 'react-i18next';
import styles from './CategoryBar.module.css';

// شريط رفيع تحت شريط التنقّل لتصفية المنتجات بالفئة، بتمرير أفقي على الجوال.
export default function CategoryBar({ categories, activeId, onSelect }) {
  const { t } = useTranslation();
  return (
    <nav className={styles.bar} aria-label={t('nav.categoriesAria')}>
      <div className={styles.scroller}>
        <button className={`${styles.chip} ${!activeId ? styles.active : ''}`} onClick={() => onSelect(null)}>
          {t('nav.categoriesAll')}
        </button>
        {categories.map((c) => (
          <button key={c.id} className={`${styles.chip} ${activeId === c.id ? styles.active : ''}`}
            onClick={() => onSelect(c.id)}>
            {c.name}
          </button>
        ))}
      </div>
    </nav>
  );
}
