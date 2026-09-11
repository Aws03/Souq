import { Link, useLocation, useSearchParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { getCategoryName } from '../product/ProductBadges';
import styles from './CategoryNav.module.css';

// شريط الفئات الأفقي تحت شريط التنقّل، على كل صفحات المتجر: الرئيسية — العروض
// — ثم فئات الـ API روابطَ تصفية. النقر على فئة ينتقل إلى الرئيسية مفلترةً بها
// (?cats=id)، فتُخفي الرئيسية أقسامها الترويجية وتعرض الكتالوج المفلتر وحده.
export default function CategoryNav({ categories = [] }) {
  const { t } = useTranslation();
  const { pathname } = useLocation();
  const [searchParams] = useSearchParams();

  const activeCats = (searchParams.get('cats') || '').split(',').map(Number).filter((n) => n > 0);
  const onHome = pathname === '/';
  const isHome = onHome && activeCats.length === 0;
  const isOffers = pathname === '/offers';
  const isCategory = (id) => onHome && activeCats.length === 1 && activeCats[0] === id;

  const cls = (active) => `${styles.link} ${active ? styles.active : ''}`;

  return (
    <nav className={styles.bar} aria-label={t('nav.categoriesAria')}>
      <div className={styles.scroller}>
        <Link to="/" className={cls(isHome)}>{t('nav.home')}</Link>
        <Link to="/offers" className={cls(isOffers)}>{t('nav.offers')}</Link>
        {categories.map((c) => (
          <Link key={c.id} to={`/?cats=${c.id}`} className={cls(isCategory(c.id))}>{getCategoryName(c)}</Link>
        ))}
      </div>
    </nav>
  );
}
