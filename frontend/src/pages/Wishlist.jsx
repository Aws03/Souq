import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useWishlist } from '../context/WishlistContext';
import { useToast } from '../context/ToastContext';
import ProductCard from '../components/product/ProductCard';
import { EmptyState } from '../components/common/StateViews';
import { PackageIcon } from '../components/icons/Icons';
import styles from './Wishlist.module.css';
import { usePageMetadata } from '../app/usePageMetadata';

// صفحة المفضّلة: تعرض ما في WishlistContext — للعميل من الخادم بأسعار الكتالوج الحيّة (المرحلة 13)، وللزائر من متصفّحه.
// فارغة افتراضياً حتى يضغط أيقونة القلب على بطاقة منتج.
export default function Wishlist() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  usePageMetadata({ title: t('wishlist.title') });
  const { items } = useWishlist();
  const toast = useToast();

  const onAdded = (name) => toast.success(t('cart.added', { name }));

  return (
    <div className="souq-layout">
      <h1 className={styles.title}>{t('wishlist.title')}</h1>

      {items.length === 0 ? (
        <EmptyState icon={PackageIcon} title={t('wishlist.emptyTitle')} message={t('wishlist.emptyMessage')}
          actionLabel={t('checkout.browseStore')} onAction={() => navigate('/')} />
      ) : (
        <div className={styles.grid}>
          {items.map((p) => <ProductCard key={p.id} product={p} onAdded={onAdded} />)}
        </div>
      )}
    </div>
  );
}
