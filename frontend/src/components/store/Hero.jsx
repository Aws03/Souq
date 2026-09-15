import { useTranslation } from 'react-i18next';
import { useStoreConfig } from '../../app/TenantProvider';
import { useStoreName } from '../../app/StoreBrand';
import { pickText } from '../../app/tenantModel';
import Button from '../common/Button';
import styles from './Hero.module.css';

// ============================================================================
// بانر الواجهة الرئيسية.
//
// كان شريطاً بثلاث شرائح دعائية مكتوبة في ملفّات الترجمة: "تسوّق بثقة، بهوية عربية أصيلة"،
// "من الإلكترونيات إلى الحرف اليدوية". نصٌّ لمتجرٍ واحد يظهر على كل متجر في المنصّة — وهذا
// نقيض white-label، لا مجرّد نصّ افتراضي. ولم يكن لأي متجر طريقة لتغييره.
//
// الآن كل ما يُعرض من إعداد المتجر نفسه: اسمه عنواناً، ووصفه الذي كتبه (seo.description)
// نصّاً، وصورته إن رفع واحدة خلفيةً. متجر لم يكتب وصفاً لا يُختلق له وصف — يظهر اسمه ودعوته
// للتسوّق وحدهما. والدعوة وحدها مترجَمة لأنها من الواجهة لا من المتجر.
//
// بانر يحرّره التاجر (شرائح، صور، روابط حملات) قدرة ناقصة في المنصّة، مسجّلة في سجلّ الدين
// مع صفحات المحتوى — لا مُدَّعاة هنا بنصّ ثابت.
// ============================================================================
export default function Hero({ targetId = 'catalog' }) {
  const { t, i18n } = useTranslation();
  const config = useStoreConfig();
  const name = useStoreName();
  const settings = config?.settings;
  const culture = settings?.locale?.defaultCulture;
  const tagline = pickText(settings?.seo?.description, i18n.language, culture);
  const image = settings?.branding?.socialImageUrl;

  const scrollToGrid = () => {
    document.getElementById(targetId)?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  };

  return (
    <section className={`${styles.hero} ${image ? '' : styles.hero__gradient}`}>
      {/* صورة المتجر خلفية — تزيينية بحتة، فالعنوان والوصف نصّ مقروء فوقها. */}
      {image && <div className={styles.hero__image} style={{ backgroundImage: `url(${image})` }} aria-hidden="true" />}
      <div className={styles.hero__overlay} />
      <div className={styles.hero__inner}>
        <h1 className={styles.hero__headline}>{name}</h1>
        {tagline && <p className={styles.hero__subline}>{tagline}</p>}
        <Button variant="saffron" size="lg" onClick={scrollToGrid}>{t('store.heroCta')}</Button>
      </div>
    </section>
  );
}
