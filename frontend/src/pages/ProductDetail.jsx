import { useEffect, useState } from 'react';
import { Link, useNavigate, useOutletContext, useParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';

import { api } from '../api/client';
import { useAuth } from '../context/AuthContext';
import { useCart } from '../context/CartContext';
import { useModule } from '../app/TenantProvider';
import Button from '../components/common/Button';
import Pagination from '../components/common/Pagination';
import Skeleton from '../components/common/Skeleton';
import { ErrorBanner } from '../components/common/StateViews';
import { CategoryBadge, PriceTag, StockBadge, getProductDescription, getProductName } from '../components/product/ProductBadges';
import { ChevronIcon } from '../components/icons/Icons';
import ProductZoom from '../components/product/ProductZoom';
import StarRating from '../components/product/StarRating';
import RatingSummary from '../components/reviews/RatingSummary';
import ReviewForm from '../components/reviews/ReviewForm';
import ReviewList from '../components/reviews/ReviewList';
import ProductSection from '../components/store/ProductSection';
import styles from './ProductDetail.module.css';
import { usePageMetadata } from '../app/usePageMetadata';

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

  const [related, setRelated] = useState(null);
  const [relatedLoading, setRelatedLoading] = useState(true);

  // أهمّ صفحة للاكتشاف: عنوانها اسم المنتج، ووصفها وصفه، وصورة مشاركتها صورته.
  // كانت كل صفحات المتجر تحمل عنوان المتجر ووصفه نفسيهما، فلا منتج يُصنَّف على اسمه
  // ولا رابط مُشارَك يُظهر ما يخصّه. القيم من المنتج نفسه — لا شيء مُخترَع هنا.
  usePageMetadata({
    title: product ? getProductName(product) : undefined,
    description: product ? getProductDescription(product) : undefined,
    image: product?.imageUrl || undefined,
    type: 'product',
  });

  useEffect(() => {
    setProduct(null); setProductError(null);
    api.getProduct(id).then(setProduct).catch((e) => setProductError(e.message));
  }, [id]);

  useEffect(() => {
    setRelated(null); setRelatedLoading(true);
    api.getRelatedProducts(id).then(setRelated).catch(() => setRelated([])).finally(() => setRelatedLoading(false));
  }, [id]);

  // وحدة التقييمات معطّلة في المتجر (المرحلة 15) ⇒ لا قسم ولا طلب يرفضه الخادم بـ 404 ModuleDisabled.
  const reviewsEnabled = useModule('reviews');
  const loadReviews = () => {
    setReviewsError(null);
    api.getProductReviews(id, { page, pageSize: 5 }).then(setReviews).catch((e) => setReviewsError(e.message));
  };
  useEffect(() => {
    if (reviewsEnabled) loadReviews();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [id, page, reviewsEnabled]);

  // الخادم يؤكّد الإضافة — الإشعار بعد نجاحها فقط؛ خطؤها (نفاد المتاح) يعرضه سياق السلة.
  const handleAdd = async () => {
    setAdding(true);
    const added = await add(product);
    setAdding(false);
    if (added) showToast(getProductName(product));
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
        {/* معرض المنتج المرتّب (الأولى رئيسية)؛ منتج بلا صور يعرض بديل الصورة. */}
        <ProductZoom images={product.images?.length ? product.images : [product.imageUrl]} videoUrl={product.videoUrl}
          productName={getProductName(product)} />
        <div>
          <CategoryBadge name={product.categoryName} />
          <h1 className={styles.name}>{getProductName(product)}</h1>
          {reviewsEnabled && reviews && reviews.totalCount > 0 && (
            <div className={styles.ratingLine}>
              <StarRating value={reviews.averageRating} />
              <span>{reviews.averageRating} ({t('product.ratingSummary', { count: reviews.totalCount })})</span>
            </div>
          )}
          <p className={styles.desc}>{getProductDescription(product)}</p>
          <StockBadge quantity={product.stockQuantity} />
          <div className={styles.buyRow}>
            <PriceTag amount={product.price} currency={product.currency} compareAt={product.compareAtPrice} />
            <Button variant="primary" loading={adding} disabled={outOfStock} onClick={handleAdd}>
              {outOfStock ? t('product.outOfStock') : t('product.addToCart')}
            </Button>
          </div>
        </div>
      </div>

      <ProductSection title={t('product.relatedTitle')} products={related} loading={relatedLoading}
        onAdded={showToast} showViewAll={false} />

      {reviewsEnabled && (
        <section className={styles.reviewsSection}>
          <h2 className={styles.sectionTitle}>{t('product.reviewsTitle')}</h2>
          {reviews && (
            <RatingSummary average={reviews.averageRating} total={reviews.totalCount} distribution={reviews.distribution} />
          )}

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
      )}
    </div>
  );
}
