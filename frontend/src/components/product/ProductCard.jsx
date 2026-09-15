import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useCart } from '../../context/CartContext';
import { useWishlist } from '../../context/WishlistContext';
import { useModule } from '../../app/TenantProvider';
import Button from '../common/Button';
import ProductImage from './ProductImage';
import { HeartIcon } from '../icons/Icons';
import { PriceTag, getProductName } from './ProductBadges';
import { productPath } from '../../features/catalog/productRouting';
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

export default function ProductCard({ product, onAdded, isNew = false, layout = 'grid' }) {
  const { t } = useTranslation();
  const { add } = useCart();
  const wishlist = useWishlist();
  const wishlistEnabled = useModule('wishlist');   // وحدة المفضّلة (المرحلة 15): معطّلة ⇒ لا قلب
  const [adding, setAdding] = useState(false);
  const outOfStock = product.stockQuantity <= 0;
  const inWishlist = wishlist.has(product.id);
  const name = getProductName(product);
  const variant = CARD_VARIANTS.includes(layout) ? layout : 'grid';
  // المضغوطة بلا زرّ إضافة: في صفّ جانبي ضيّق الزرّ يزاحم الاسم ويُضغط خطأً.
  const showAddButton = variant !== 'compact';

  // الخادم يؤكّد الإضافة (منشور، متاح) — الإشعار بعد نجاحها فقط؛ خطؤها يعرضه سياق السلة.
  const handleAdd = async () => {
    setAdding(true);
    const added = await add(product);
    setAdding(false);
    if (added) onAdded?.(name);
  };

  return (
    <article className={`${styles.card} ${styles[variant]}`}>
      <Link to={productPath(product)} className={styles.media} aria-label={name}>
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
          <Link to={productPath(product)} className={styles.nameLink}>{name}</Link>
        </h3>
        <div className={styles.foot}>
          <PriceTag amount={product.price} currency={product.currency} compareAt={product.compareAtPrice} />
          {showAddButton && (
            <Button variant="primary" size="sm" loading={adding} disabled={outOfStock} onClick={handleAdd}>
              {outOfStock ? t('product.outOfStock') : t('product.addToCart')}
            </Button>
          )}
        </div>
      </div>
    </article>
  );
}
