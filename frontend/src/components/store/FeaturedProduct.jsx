import { useTranslation } from 'react-i18next';
import ProductCard from '../product/ProductCard';
import Skeleton from '../common/Skeleton';
import styles from './FeaturedProduct.module.css';

// ============================================================================
// صدارة تحريرية: منتج واحد في مساحة كبيرة بجانب ثلاثة مضغوطة.
//
// لماذا؟ شبكة من أربعة أعمدة متطابقة تُعامل كل منتج بالوزن نفسه، فلا شيء يبرز ولا شيء يُروى.
// تخطيط غير متماثل يُعطي المتجر موضعاً يقول فيه "ابدأ من هنا" — وهو ما يميّز واجهةً تجارية عن
// شبكة نتائج.
//
// وتبقى مقروءة: بطاقة الصدارة هي المكوّن نفسه بصيغة featured — لا منطق مكرّر، وكل شيء عنها
// (السعر، التوفّر، المفضّلة) يتصرّف كما في بقيّة المتجر.
//
// متجرٌ بأقلّ من منتجين لا يرى هذا القسم: صدارة بلا ما يجاورها ليست صدارة.
// ============================================================================
export default function FeaturedProduct({ title, products, loading, onAdded }) {
  const { t } = useTranslation();

  if (loading) {
    return (
      <section className={styles.section}>
        <div className="souq-layout">
          <Skeleton height={320} radius={14} />
        </div>
      </section>
    );
  }

  if (!products || products.length < 2) return null;

  const [lead, ...rest] = products.slice(0, 4);

  return (
    <section className={styles.section}>
      <div className="souq-layout">
        <div className={styles.head}>
          <h2 className={styles.title}>{title}</h2>
          <span className={styles.rule} aria-hidden="true" />
        </div>

        <div className={styles.grid}>
          <div className={styles.lead}>
            <ProductCard product={lead} onAdded={onAdded} layout="featured" />
          </div>
          <div className={styles.rail}>
            {rest.map((product) => (
              <ProductCard key={product.id} product={product} onAdded={onAdded} layout="list" />
            ))}
          </div>
        </div>
      </div>
      <span className="souq-visually-hidden">{t('store.featuredAria', { count: products.length })}</span>
    </section>
  );
}
