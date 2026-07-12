import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useCart } from '../../context/CartContext';
import { useWishlist } from '../../context/WishlistContext';
import Button from '../common/Button';
import ProductImage from './ProductImage';
import { HeartIcon } from '../icons/Icons';
import { PriceTag, getProductName } from './ProductBadges';
import styles from './ProductCard.module.css';

const ADD_FEEDBACK_MS = 350;

// بطاقة منتج نظيفة (نمط الكتالوج المرجعي): صورة كاملة بلا قصّ، الاسم، السعر،
// زرّ الإضافة — بلا شارة فئة ولا وصف. قلب المفضّلة فوق الصورة. النقر على الصورة
// أو الاسم ينتقل لصفحة المنتج. layout="list" يبدّلها لبطاقة أفقية (وضع القائمة).
export default function ProductCard({ product, onAdded, isNew = false, layout = 'grid' }) {
  const { t } = useTranslation();
  const { add } = useCart();
  const wishlist = useWishlist();
  const [adding, setAdding] = useState(false);
  const outOfStock = product.stockQuantity <= 0;
  const inWishlist = wishlist.has(product.id);
  const name = getProductName(product);

  const handleAdd = () => {
    setAdding(true);
    add(product);
    setTimeout(() => { setAdding(false); onAdded?.(name); }, ADD_FEEDBACK_MS);
  };

  return (
    <article className={`${styles.card} ${layout === 'list' ? styles.cardList : ''}`}>
      <Link to={`/products/${product.id}`} className={styles.media} aria-label={name}>
        <ProductImage product={product} fit="contain" />
        {isNew && <span className={styles.badge}>{t('product.badgeNew')}</span>}
      </Link>

      <button type="button" className={styles.wishBtn} onClick={() => wishlist.toggle(product)}
        aria-pressed={inWishlist} aria-label={t(inWishlist ? 'product.wishlistRemove' : 'product.wishlistAdd')}>
        <HeartIcon size={16} filled={inWishlist} />
      </button>

      <div className={styles.body}>
        <h3 className={styles.name}>
          <Link to={`/products/${product.id}`} className={styles.nameLink}>{name}</Link>
        </h3>
        <div className={styles.foot}>
          <PriceTag amount={product.price} currency={product.currency} />
          <Button variant="primary" size="sm" loading={adding} disabled={outOfStock} onClick={handleAdd}>
            {outOfStock ? t('product.outOfStock') : t('product.addToCart')}
          </Button>
        </div>
      </div>
    </article>
  );
}
