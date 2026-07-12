import { useEffect, useState } from 'react';
import { Link, useNavigate, useOutletContext, useParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';

import { api } from '../api/client';
import { useAuth } from '../context/AuthContext';
import { useCart } from '../context/CartContext';
import Button from '../components/common/Button';
import Pagination from '../components/common/Pagination';
import Skeleton from '../components/common/Skeleton';
import { ErrorBanner } from '../components/common/StateViews';
import { CategoryBadge, PriceTag, StockBadge, getProductName } from '../components/product/ProductBadges';
import { ChevronIcon } from '../components/icons/Icons';
import ProductZoom from '../components/product/ProductZoom';
import StarRating from '../components/product/StarRating';
import ReviewForm from '../components/reviews/ReviewForm';
import ReviewList from '../components/reviews/ReviewList';
import styles from './ProductDetail.module.css';

// صفحة تفصيل منتج: صورة كبيرة + بيانات كاملة + إضافة للسلة، وأسفلها التقييمات
// (متوسط + قائمة مرقّمة + نموذج إضافة تقييم لمن يحقّ له).
export default function ProductDetail() {
  const { t } = useTranslation();
  const { id } = useParams();
  const navigate = useNavigate();
  const { isAuthenticated } = useAuth();
  const { add } = useCart();
  const { showToast } = useOutletContext();

  const [product, setProduct] = useState(null);
  const [productError, setProductError] = useState(null);
  const [adding, setAdding] = useState(false);

  const [reviews, setReviews] = useState(null);
  const [reviewsError, setReviewsError] = useState(null);
  const [page, setPage] = useState(1);

  useEffect(() => {
    setProduct(null); setProductError(null);
    api.getProduct(id).then(setProduct).catch((e) => setProductError(e.message));
  }, [id]);

  const loadReviews = () => {
    setReviewsError(null);
    api.getProductReviews(id, { page, pageSize: 5 }).then(setReviews).catch((e) => setReviewsError(e.message));
  };
  useEffect(loadReviews, [id, page]);

  const handleAdd = () => {
    setAdding(true);
    add(product);
    setTimeout(() => { setAdding(false); showToast(getProductName(product)); }, 350);
  };

  if (productError) return <div className="souq-layout"><ErrorBanner message={productError} /></div>;
  if (!product) {
    return (
      <div className="souq-layout">
        <div className={styles.grid}>
          <Skeleton height={360} radius={14} />
          <div><Skeleton height={30} width="70%" /></div>
        </div>
      </div>
    );
  }

  const outOfStock = product.stockQuantity <= 0;
  const totalPages = reviews ? Math.ceil(reviews.totalCount / reviews.pageSize) : 1;

  return (
    <div className="souq-layout">
      {/* رابط الرجوع أعلى الصفحة (لا في أسفلها بعد التقييمات) — ظاهر فوراً على
          الجوال وسطح المكتب بلا تمرير، وفي مسار الصفحة الطبيعي فلا يزاحم شيئاً. */}
      <button type="button" className={styles.backLink} onClick={() => navigate('/')}>
        <ChevronIcon dir="end" size={16} /> {t('product.backToStore')}
      </button>
      <div className={styles.grid}>
        <ProductZoom images={[product.imageUrl]} videoUrl={product.videoUrl} productName={getProductName(product)} />
        <div>
          <CategoryBadge name={product.categoryName} />
          <h1 className={styles.name}>{getProductName(product)}</h1>
          {reviews && reviews.totalCount > 0 && (
            <div className={styles.ratingLine}>
              <StarRating value={reviews.averageRating} />
              <span>{reviews.averageRating} ({t('product.ratingSummary', { count: reviews.totalCount })})</span>
            </div>
          )}
          <p className={styles.desc}>{product.description}</p>
          <StockBadge quantity={product.stockQuantity} />
          <div className={styles.buyRow}>
            <PriceTag amount={product.price} currency={product.currency} />
            <Button variant="primary" loading={adding} disabled={outOfStock} onClick={handleAdd}>
              {outOfStock ? t('product.outOfStock') : t('product.addToCart')}
            </Button>
          </div>
        </div>
      </div>

      <section className={styles.reviewsSection}>
        <h2 className={styles.sectionTitle}>{t('product.reviewsTitle')}</h2>

        {isAuthenticated ? (
          <ReviewForm productId={id} onSubmitted={() => { setPage(1); loadReviews(); }} />
        ) : (
          <p className={styles.signInHint}>
            <Link to="/login">{t('auth.signIn')}</Link> {t('product.signInToReview')}
          </p>
        )}

        <ReviewList reviews={reviews?.items ?? []} loading={!reviews && !reviewsError}
          error={reviewsError} onRetry={loadReviews} />
        <Pagination page={page} totalPages={totalPages} onChange={setPage} />
      </section>
    </div>
  );
}
