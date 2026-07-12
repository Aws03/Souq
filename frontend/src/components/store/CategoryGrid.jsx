import { useTranslation } from 'react-i18next';
import { TagIcon, PackageIcon, GridIcon, PercentIcon } from '../icons/Icons';
import styles from './CategoryGrid.module.css';

const ICONS = [TagIcon, PackageIcon, GridIcon, PercentIcon];

// صف من 4 بطاقات فئة أسفل البانر — اختصار مباشر للتصفّح، بديل عن شرائح فئات
// FilterBar فقط. النقر يطبّق فلتر الفئة نفسه ثم يمرّر لشبكة المنتجات.
export default function CategoryGrid({ categories, onSelect, targetId = 'product-grid' }) {
  const { t } = useTranslation();
  if (!categories.length) return null;

  const handleClick = (id) => {
    onSelect(id);
    document.getElementById(targetId)?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  };

  return (
    <section className={styles.category}>
      <div className={`souq-layout ${styles.category__grid}`}>
        {categories.slice(0, 4).map((c, i) => {
          const Icon = ICONS[i % ICONS.length];
          return (
            <button key={c.id} type="button" className={styles.category__card} onClick={() => handleClick(c.id)}>
              <span className={styles.category__icon}><Icon size={26} /></span>
              <span className={styles.category__name}>{c.name}</span>
              <span className={styles.category__cta}>{t('store.shopNow')}</span>
            </button>
          );
        })}
      </div>
    </section>
  );
}
