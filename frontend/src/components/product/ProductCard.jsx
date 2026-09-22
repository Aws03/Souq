import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useCart } from '../../context/CartContext';
import { useWishlist } from '../../context/WishlistContext';
import { useModule } from '../../app/TenantProvider';
import Button from '../common/Button';
import ProductImage from './ProductImage';
import { HeartIcon } from '../icons/Icons';
import { PriceTag, getProductName } from './ProductBadges';
import { reportClick, reportImpression } from '../../features/analytics';
import { productPath } from '../../features/catalog/productRouting';
import { hasVariantChoice } from '../../features/catalog/variantSelection';
import styles from './ProductCard.module.css';

// بطاقة منتج نظيفة (نمط الكتالوج المرجعي): صورة كاملة بلا قصّ، الاسم، السعر،
// ============================================================================
// بطاقة المنتج بأربع صيغ، وكلّها المكوّن نفسه — منطق الإضافة والمفضّلة والتوفّر يعيش مرّة واحدة:
//
//   grid     — الافتراضية: شبكة الكتالوج.
//   list     — أفقية لوضع القائمة.
//   compact  — للصفوف الجانبية والمساحات الضيّقة: بلا زرّ إضافة، الصورة والاسم والسعر فقط.
//   featured — بطاقة أكبر لصدارة الصفحة: مساحة أوسع للصورة، ووصف قصير.
//
// صيغةٌ زائدة تعني شيفرة تتعفّن، فالقائمة مقصورة على ما يُستعمل فعلاً. والنقر على الصورة أو
// الاسم ينتقل لصفحة المنتج في كل الصيغ.
// ============================================================================
export const CARD_VARIANTS = ['grid', 'list', 'compact', 'featured'];

// listId/position: هويّةُ القائمة التي ظهرت فيها البطاقة وموضعُها فيها (C9b، ADR-0050 §3).
// اختياريّان: بطاقةٌ خارج قائمةٍ معروفة (المفضّلة مثلاً) لا تُقاس، ولا تكسر.
export default function ProductCard({ product, onAdded, isNew = false, layout = 'grid', listId = null, position = null }) {
  const { t } = useTranslation();
  const { add } = useCart();
  const wishlist = useWishlist();
  const wishlistEnabled = useModule('wishlist');   // وحدة المفضّلة (المرحلة 15): معطّلة ⇒ لا قلب
  const [adding, setAdding] = useState(false);
  const outOfStock = product.stockQuantity <= 0;
  // منتج بأكثر من متغيّر معروض يُختار متغيّره في صفحته (P-08c): البطاقة تقود إليها بدل إضافة يرفضها الخادم
  // (VariantRequired). الشرط من الخادم (variantChoiceRequired) لا من السعر ولا من تخمين في الواجهة.
  const needsChoice = product.variantChoiceRequired || hasVariantChoice(product);
  const inWishlist = wishlist.has(product.id);
  const name = getProductName(product);
  const variant = CARD_VARIANTS.includes(layout) ? layout : 'grid';
  // المضغوطة بلا زرّ إضافة: في صفّ جانبي ضيّق الزرّ يزاحم الاسم ويُضغط خطأً.
  const showAddButton = variant !== 'compact';

  // ==========================================================================
  // الظهورُ يُبلَّغ عند تركيب البطاقة، والنقرةُ عند اتّباع رابطها (C9b).
  //
  // **وما يُقاس هنا هو الترتيب لا الرؤية**: بلا مُراقِب تقاطعٍ لا نعرف ما وقع في نافذة العرض
  // فعلاً، وادّعاءُ ذلك يُنتج رقماً يبدو دقيقاً وليس كذلك. فالمقصودُ صراحةً «عُرض في القائمة
  // بهذا الموضع»، وهو ما يكفي لنسبة النقر ولترتيب النتائج — وهما ما وُجد القياس لأجلهما.
  // ==========================================================================
  useEffect(() => {
    reportImpression(listId, product.id, position);
  }, [listId, product.id, position]);

  const handleOpen = () => reportClick(listId, product.id, position);

  // الخادم يؤكّد الإضافة (منشور، متاح) — الإشعار بعد نجاحها فقط؛ خطؤها يعرضه سياق السلة.
  const handleAdd = async () => {
    setAdding(true);
    const added = await add(product);
    setAdding(false);
    if (added) onAdded?.(name);
  };

  return (
    <article className={`${styles.card} ${styles[variant]}`}>
      <Link to={productPath(product)} className={styles.media} aria-label={name} onClick={handleOpen}>
        {/* بطاقة الصدارة فوق الطيّة: تُحمَّل بأولوية كي لا تؤخّر أكبر عنصر مرئي. */}
        <ProductImage product={product} fit="contain" priority={variant === 'featured'} />
        {isNew && <span className={styles.badge}>{t('product.badgeNew')}</span>}
      </Link>

      {wishlistEnabled && (
        <button type="button" className={styles.wishBtn} onClick={() => wishlist.toggle(product)}
          aria-pressed={inWishlist} aria-label={t(inWishlist ? 'product.wishlistRemove' : 'product.wishlistAdd')}>
          <HeartIcon size={16} filled={inWishlist} />
        </button>
      )}

      <div className={styles.body}>
        <h3 className={styles.name}>
          <Link to={productPath(product)} className={styles.nameLink} onClick={handleOpen}>{name}</Link>
        </h3>
        <div className={styles.foot}>
          <PriceTag amount={product.price} currency={product.currency} compareAt={product.compareAtPrice}
            from={!!product.priceIsFrom} />
          {/* منتج بخيارات: رابط لصفحته (اختيار المتغيّر هناك) لا زرّ إضافة يفترض مقاساً — ورابطٌ لا زرّ لأنه انتقال. */}
          {showAddButton && (needsChoice ? (
            <Link to={productPath(product)} className={styles.chooseLink} onClick={handleOpen}>
              {outOfStock ? t('product.outOfStock') : t('product.chooseOptions')}
            </Link>
          ) : (
            <Button variant="primary" size="sm" loading={adding} disabled={outOfStock} onClick={handleAdd}>
              {outOfStock ? t('product.outOfStock') : t('product.addToCart')}
            </Button>
          ))}
        </div>
      </div>
    </article>
  );
}
