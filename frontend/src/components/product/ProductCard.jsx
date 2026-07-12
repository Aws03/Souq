import { useEffect, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useCart } from '../../context/CartContext';
import { useWishlist } from '../../context/WishlistContext';
import Button from '../common/Button';
import ProductImage from './ProductImage';
import { HeartIcon, SearchIcon, CloseIcon } from '../icons/Icons';
import { CategoryBadge, PriceTag, StockBadge, getProductName } from './ProductBadges';
import styles from './ProductCard.module.css';

const ADD_FEEDBACK_MS = 350;

// بطاقة منتج واحدة قابلة لإعادة الاستخدام. isNew يعرض شارة "جديد" (تُمرَّر من
// سياق يعرف فعلاً أن المنتج حديث، كصف "وصل حديثاً"). ctaVariant يبدّل زر
// التذييل: "أضف للسلة" (الشبكة الرئيسية) أو "عرض المنتج" (صفوف الاكتشاف).
// النقر على الصورة نفسها يفتح صندوق تكبير (Lightbox) بدل الانتقال المباشر —
// التنقّل لصفحة المنتج يبقى متاحاً عبر اسم المنتج.
export default function ProductCard({ product, onAdded, isNew = false, ctaVariant = 'addToCart' }) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { add } = useCart();
  const wishlist = useWishlist();
  const [adding, setAdding] = useState(false);
  const [lightboxOpen, setLightboxOpen] = useState(false);
  const outOfStock = product.stockQuantity <= 0;
  const inWishlist = wishlist.has(product.id);
  const name = getProductName(product);

  useEffect(() => {
    if (!lightboxOpen) return;
    const onKeyDown = (e) => { if (e.key === 'Escape') setLightboxOpen(false); };
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [lightboxOpen]);

  const handleAdd = () => {
    setAdding(true);
    add(product);
    setTimeout(() => { setAdding(false); onAdded?.(name); }, ADD_FEEDBACK_MS);
  };

  return (
    <article className={styles.card}>
      <div className={styles.media}>
        <button type="button" className={styles.mediaLink} onClick={() => setLightboxOpen(true)} aria-label={name}>
          <ProductImage product={product} />
        </button>
        {isNew && <span className={styles.badge}>{t('product.badgeNew')}</span>}
        <div className={styles.cardActions}>
          <button
            type="button"
            className={styles.iconOverlayBtn}
            onClick={() => wishlist.toggle(product)}
            aria-pressed={inWishlist}
            aria-label={t(inWishlist ? 'product.wishlistRemove' : 'product.wishlistAdd')}
          >
            <HeartIcon size={16} filled={inWishlist} />
          </button>
          <button
            type="button"
            className={`${styles.iconOverlayBtn} ${styles.zoomBtn}`}
            onClick={() => setLightboxOpen(true)}
            aria-label={t('product.zoomAria')}
          >
            <SearchIcon size={16} />
          </button>
        </div>
      </div>
      <div className={styles.body}>
        <CategoryBadge name={product.categoryName} />
        <h3 className={styles.name}><Link to={`/products/${product.id}`} className={styles.nameLink}>{name}</Link></h3>
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

      {lightboxOpen && (
        <div className={styles.lightboxOverlay} onClick={() => setLightboxOpen(false)}>
          <button type="button" className={styles.lightboxClose} onClick={() => setLightboxOpen(false)} aria-label={t('common.close')}>
            <CloseIcon size={20} />
          </button>
          <div className={styles.lightboxImage} onClick={(e) => e.stopPropagation()}>
            <ProductImage product={product} />
          </div>
        </div>
      )}
    </article>
  );
}
