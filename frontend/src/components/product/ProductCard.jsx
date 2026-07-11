import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useCart } from '../../context/CartContext';
import { useWishlist } from '../../context/WishlistContext';
import Button from '../common/Button';
import ProductImage from './ProductImage';
import { HeartIcon } from '../icons/Icons';
import { CategoryBadge, PriceTag, StockBadge } from './ProductBadges';
import styles from './ProductCard.module.css';

const ADD_FEEDBACK_MS = 350;

// بطاقة منتج واحدة قابلة لإعادة الاستخدام. isNew يعرض شارة "جديد" (تُمرَّر من
// سياق يعرف فعلاً أن المنتج حديث، كصف "وصل حديثاً"). ctaVariant يبدّل زر
// التذييل: "أضف للسلة" (الشبكة الرئيسية) أو "عرض المنتج" (صفوف الاكتشاف).
export default function ProductCard({ product, onAdded, isNew = false, ctaVariant = 'addToCart' }) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { add } = useCart();
  const wishlist = useWishlist();
  const [adding, setAdding] = useState(false);
  const outOfStock = product.stockQuantity <= 0;
  const inWishlist = wishlist.has(product.id);

  const handleAdd = () => {
    setAdding(true);
    add(product);
    setTimeout(() => { setAdding(false); onAdded?.(product.name); }, ADD_FEEDBACK_MS);
  };

  return (
    <article className={styles.card}>
      <div className={styles.media}>
        <Link to={`/products/${product.id}`} className={styles.mediaLink}><ProductImage product={product} /></Link>
        {isNew && <span className={styles.badge}>{t('product.badgeNew')}</span>}
        <button
          type="button"
          className={styles.wishlistBtn}
          onClick={() => wishlist.toggle(product)}
          aria-pressed={inWishlist}
          aria-label={t(inWishlist ? 'product.wishlistRemove' : 'product.wishlistAdd')}
        >
          <HeartIcon size={16} filled={inWishlist} />
        </button>
      </div>
      <div className={styles.body}>
        <CategoryBadge name={product.categoryName} />
        <h3 className={styles.name}><Link to={`/products/${product.id}`} className={styles.nameLink}>{product.name}</Link></h3>
        <p className={styles.desc}>{product.description}</p>
        <StockBadge quantity={product.stockQuantity} />
        <div className={styles.foot}>
          <PriceTag amount={product.price} currency={product.currency} />
          {ctaVariant === 'view' ? (
            <Button variant="primary" size="sm" onClick={() => navigate(`/products/${product.id}`)}>
              {t('product.viewProduct')}
            </Button>
          ) : (
            <Button variant="primary" size="sm" loading={adding} disabled={outOfStock} onClick={handleAdd}>
              {outOfStock ? t('product.outOfStock') : t('product.addToCart')}
            </Button>
          )}
        </div>
      </div>
    </article>
  );
}
