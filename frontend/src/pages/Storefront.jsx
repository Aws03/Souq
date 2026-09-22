import { useSearchParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import Hero from '../components/store/Hero';
import ProductSection from '../components/store/ProductSection';
import FeaturedProduct from '../components/store/FeaturedProduct';
import Catalog from '../components/catalog/Catalog';
import styles from './Storefront.module.css';

// الترتيب الافتراضي — نسخةُ `StoreSections.Types` في النطاق، للنافذة السابقة لوصول الإعداد وحدها.
export const DEFAULT_SECTIONS = ['hero', 'featured', 'newArrivals', 'offers', 'catalog'];

// ============================================================================
// الصفحة الرئيسية — **قائمةُ أقسامٍ موصوفة، لا ترتيبٌ مكتوبٌ في JSX** (C8، ADR-0060).
//
// كانت هذه الصفحة تعرف ترتيبها بنفسها: بانرٌ ثمّ صدارةٌ ثمّ صفّان ثمّ الكتالوج، لكلّ متجر. وكانت
// المكوّناتُ تأخذ كلَّ ما تعرضه وسائطَ أصلاً — أي أنّها كانت **عارضاتٍ تنتظر وصفاً** منذ البداية.
// الآن يأتي الوصفُ من إعداد المتجر: قائمةٌ مرتَّبةٌ من أنواعٍ يتحقّق منها الخادم.
//
// **والسجلُّ هنا ترجمةٌ لا قرار**: الخادم يقرّر أيُّ الأقسام تُعرض وبأيّ ترتيب (`enabledSections`)،
// وهذا الملفّ يعرف فقط كيف يُرسَم كلُّ نوع. ونوعٌ لا يعرفه هذا السجلّ يُتخطّى بصمت — نسخةُ واجهةٍ
// أقدم من الخادم ترسم ما تفهمه بدل أن تسقط الصفحةُ كلُّها.
//
// وما لم يتغيّر: **الفلترةُ تُخفي الأقسام الترويجية**. النقرُ على فئةٍ أو كتابةُ بحثٍ يجعل الصفحة
// قائمةَ كتالوجٍ نظيفة — وهو سلوكٌ يسبق هذه المرحلة ويبقى بعدها، فالكتالوجُ وحده يُرسم حينئذ
// مهما قال الترتيب.
// ============================================================================
export default function Storefront({
  categories, onAdded, refreshKey, sections,
  newArrivals, newArrivalsLoading, bestSellers, bestSellersLoading, offers, offersLoading,
}) {
  const { t } = useTranslation();
  const [searchParams] = useSearchParams();

  // 'q' ضمنها: البحث حالة رابط كبقية الفلاتر (searchRouting).
  const hasActiveView = ['cats', 'min', 'max', 'sort', 'page', 'q'].some((k) => searchParams.get(k));

  // **ما قبل وصول الإعداد** يُرسم بالترتيب الافتراضي، لا بصفحةٍ ناقصة: بين أوّل رسمٍ ووصول
  // `/api/storefront/config` نافذةٌ، وعرضُ الكتالوج وحده فيها ثمّ ظهورُ البانر فجأةً وميضٌ
  // يراه كلُّ زائر. والنسخةُ هنا هي نسخةُ `StoreSections.Types` نفسها، بالدور نفسه الذي تؤدّيه
  // رموزُ `styles.css` قبل وصول الهوية — ويحرس تطابقَهما اختبارٌ.
  const ordered = sections?.length ? sections : DEFAULT_SECTIONS;

  // والكتالوجُ يُرسم دائماً وإن سقط من الوصف: الخادم يمنع إطفاءه، وواجهةٌ تثق بذلك وحده تعرض
  // صفحةً بلا منتجات لو وصلها وصفٌ ناقص من نسخةٍ أقدم.
  const visible = ordered.includes('catalog') ? ordered : [...ordered, 'catalog'];

  const renderers = {
    hero: () => <Hero key="hero" targetId="catalog" />,

    // صدارة تحريرية: موضعٌ واحد يقول "ابدأ من هنا" بدل أربعة أعمدة متساوية الوزن. تُخفي نفسها
    // في متجر بأقلّ من منتجين.
    featured: () => (
      <FeaturedProduct key="featured" title={t('store.featuredTitle')} products={bestSellers}
        loading={bestSellersLoading} onAdded={onAdded} />
    ),

    newArrivals: () => (
      <ProductSection key="newArrivals" title={t('store.newArrivals')} products={newArrivals}
        loading={newArrivalsLoading} onAdded={onAdded} isNew viewAllTargetId="catalog" />
    ),

    // صفّ العروض: منتجات مخفّضة فعلاً (onSale في الخادم). ProductSection يخفي نفسه حين لا نتائج،
    // فمتجر بلا تخفيضات لا يعرض صفّاً اسمه "عروض" فيه منتجات عادية.
    offers: () => (
      <ProductSection key="offers" title={t('nav.offers')} products={offers} loading={offersLoading}
        onAdded={onAdded} viewAllHref="/offers" />
    ),

    catalog: () => (
      <div key="catalog" className={`souq-layout ${styles.section}`} id="catalog">
        <h2 className={styles.allProductsTitle}>{t('store.allProducts')}</h2>
        <Catalog categories={categories} onAdded={onAdded} refreshKey={refreshKey} />
      </div>
    ),
  };

  return <>{visible.map((type) => (hasActiveView && type !== 'catalog' ? null : renderers[type]?.()))}</>;
}
