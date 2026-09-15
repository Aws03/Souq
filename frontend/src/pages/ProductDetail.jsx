import { useEffect, useMemo, useRef, useState } from 'react';
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
import Stepper from '../components/common/Stepper';
import { isProductId, needsCanonicalRedirect, productPath } from '../features/catalog/productRouting';
import { breadcrumbStructuredData, productStructuredData } from '../app/structuredData';
import { useStructuredData } from '../app/useStructuredData';
import { canonicalUrl } from '../app/pageMetadata';
import { usePageMetadata } from '../app/usePageMetadata';
import styles from './ProductDetail.module.css';

// صفحة تفصيل منتج: صورة كبيرة + بيانات كاملة + إضافة للسلة، وأسفلها التقييمات
// (متوسط + قائمة مرقّمة + نموذج إضافة تقييم لمن يحقّ له).
export default function ProductDetail() {
  const { t } = useTranslation();
  const { handle } = useParams();
  const navigate = useNavigate();
  const { isAuthenticated } = useAuth();
  const { add } = useCart();
  const { showToast } = useOutletContext();

  const [product, setProduct] = useState(null);
  const [productError, setProductError] = useState(null);
  const [adding, setAdding] = useState(false);
  const [quantity, setQuantity] = useState(1);

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

  // الرابط قد يحمل الاسم (القانوني) أو المعرّف (روابط قديمة ومشاركات سابقة) — كلاهما يعمل.
  // والمنتج الذي بين أيدينا يُعرَف بأيّهما، فالتحويل إلى الشكل القانوني لا يُعيد تحميله.
  const held = useRef(null);
  useEffect(() => {
    const current = held.current;
    if (current && (current.slug === handle || String(current.id) === String(handle))) return undefined;

    let active = true;
    held.current = null;
    setProduct(null); setProductError(null); setQuantity(1);
    const load = isProductId(handle) ? api.getProduct(handle) : api.getProductBySlug(handle);
    load.then((p) => { if (active) { held.current = p; setProduct(p); } })
      .catch((e) => { if (active) setProductError(e.message); });
    return () => { active = false; };
  }, [handle]);

  // وصل المنتج بمعرّفه ⇒ نُبدّل الرابط إلى شكله القانوني بلا إضافة خطوة في سجلّ الرجوع.
  useEffect(() => {
    if (needsCanonicalRedirect(handle, product)) navigate(productPath(product), { replace: true });
  }, [handle, product, navigate]);

  // ما بعد التحميل يُطلب بمعرّف المنتج لا بما في الرابط: نقاط التقييمات والمشابهات تعرف المعرّف وحده.
  const productId = product?.id ?? null;

  useEffect(() => {
    if (productId === null) return undefined;
    let active = true;
    setRelated(null); setRelatedLoading(true);
    api.getRelatedProducts(productId)
      .then((list) => { if (active) setRelated(list); })
      .catch(() => { if (active) setRelated([]); })
      .finally(() => { if (active) setRelatedLoading(false); });
    return () => { active = false; };
  }, [productId]);

  // وحدة التقييمات معطّلة في المتجر (المرحلة 15) ⇒ لا قسم ولا طلب يرفضه الخادم بـ 404 ModuleDisabled.
  const reviewsEnabled = useModule('reviews');
  const loadReviews = () => {
    if (productId === null) return;
    setReviewsError(null);
    api.getProductReviews(productId, { page, pageSize: 5 }).then(setReviews).catch((e) => setReviewsError(e.message));
  };
  useEffect(() => {
    setReviews(null);
    if (reviewsEnabled) loadReviews();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [productId, page, reviewsEnabled]);

  // الخادم يؤكّد الإضافة — الإشعار بعد نجاحها فقط؛ خطؤها (نفاد المتاح) يعرضه سياق السلة.
  const handleAdd = async () => {
    setAdding(true);
    const added = await add(product, quantity);
    setAdding(false);
    if (added) showToast(getProductName(product));
  };

  // بيانات منظّمة للمحرّكات — بعد وصول المنتج فقط، وكل حقل فيها من الخادم.
  const trail = useMemo(() => {
    if (!product) return [];
    const origin = window.location.origin;
    return [
      { name: t('nav.home'), url: `${origin}/` },
      ...(product.categoryName ? [{ name: product.categoryName, url: `${origin}/?cats=${product.categoryId}` }] : []),
      { name: getProductName(product), url: canonicalUrl(origin, productPath(product), '') },
    ];
  }, [product, t]);

  useStructuredData(
    productStructuredData({
      product,
      url: product ? canonicalUrl(window.location.origin, productPath(product), '') : '',
      name: product ? getProductName(product) : '',
      description: product ? getProductDescription(product) : '',
      image: product?.imageUrl,
      rating: reviews ? { average: reviews.averageRating, count: reviews.totalCount } : null,
    }),
    breadcrumbStructuredData(trail),
  );

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
  const name = getProductName(product);

  return (
    <div className="souq-layout">
      {/* فتات الخبز بدل زرّ "رجوع للمتجر" وحده: يقول أين نحن، ويعطي طريقاً إلى فئة المنتج
          لا إلى الرئيسية فقط — وهو نفس المسار الذي يُنشر كبيان منظّم أدناه. */}
      <nav className={styles.breadcrumb} aria-label={t('product.breadcrumbAria')}>
        <Link to="/">{t('nav.home')}</Link>
        <ChevronIcon dir="end" size={14} />
        {product.categoryName && (
          <>
            <Link to={`/?cats=${product.categoryId}`}>{product.categoryName}</Link>
            <ChevronIcon dir="end" size={14} />
          </>
        )}
        <span aria-current="page">{name}</span>
      </nav>
      <div className={styles.grid}>
        {/* معرض المنتج المرتّب (الأولى رئيسية)؛ منتج بلا صور يعرض بديل الصورة. */}
        <ProductZoom images={product.images?.length ? product.images : [product.imageUrl]} videoUrl={product.videoUrl}
          productName={name} />
        <div>
          <CategoryBadge name={product.categoryName} />
          <h1 className={styles.name}>{name}</h1>
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
            {/* شراء قطعتين كان يعني الضغط مرّتين ثم فتح السلة للتأكّد. الحدّ الأعلى هو المتاح
                الآن على الخادم — والخادم يبقى الفاصل: ردّه هو ما يدخل السلة. */}
            {!outOfStock && (
              <Stepper value={quantity} max={product.stockQuantity}
                onInc={() => setQuantity((q) => Math.min(q + 1, product.stockQuantity))}
                onDec={() => setQuantity((q) => Math.max(1, q - 1))} />
            )}
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
            <ReviewForm productId={productId} onSubmitted={() => { setPage(1); loadReviews(); }} />
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
