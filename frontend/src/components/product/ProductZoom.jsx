import { useEffect, useMemo, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { useTranslation } from 'react-i18next';
import { SearchIcon, CloseIcon, ChevronIcon, ExpandIcon, CameraIcon } from '../icons/Icons';
import { isRealImage } from './ProductImage';
import styles from './ProductZoom.module.css';

const MIN_ZOOM = 1.5;
const MAX_ZOOM = 3;
const ZOOM_STEP = 0.5;
const DEFAULT_ZOOM = 2.5;
const MAX_THUMBNAILS = 5;

const clamp = (n, min, max) => Math.min(max, Math.max(min, n));

// عارض صورة/فيديو منتج بتكبير في المكان: عند التمرير تظهر طبقة مكبَّرة فوق
// الصورة نفسها (نفس الموضع والحجم) تتتبّع المؤشّر — لا لوحة جانبية. + أزرار
// تكبير/تصغير + صف مصغّرات + صندوق عرض كامل الشاشة. الجوال يستبدل تكبير
// التمرير (لا مؤشّر فأرة) بفتح الصندوق مباشرة عند اللمس.
export default function ProductZoom({ images, videoUrl, productName }) {
  const { t } = useTranslation();
  const galleryImages = useMemo(
    () => (images && images.length > 0 ? images.filter(isRealImage) : []),
    [images]
  );

  const [activeTab, setActiveTab] = useState('photo');
  const [activeIndex, setActiveIndex] = useState(0);
  const [zoomLevel, setZoomLevel] = useState(DEFAULT_ZOOM);
  const [hovering, setHovering] = useState(false);
  const [cursorPct, setCursorPct] = useState({ x: 50, y: 50 });
  const [lightboxOpen, setLightboxOpen] = useState(false);
  const imageBoxRef = useRef(null);

  const isTouchDevice = useMemo(
    () => typeof window !== 'undefined' && window.matchMedia('(hover: none)').matches,
    []
  );

  const hasImages = galleryImages.length > 0;
  const hasVideo = !!videoUrl;
  const activeImage = hasImages ? galleryImages[activeIndex] : null;

  const handleMouseMove = (e) => {
    const rect = imageBoxRef.current.getBoundingClientRect();
    const x = clamp(((e.clientX - rect.left) / rect.width) * 100, 0, 100);
    const y = clamp(((e.clientY - rect.top) / rect.height) * 100, 0, 100);
    setCursorPct({ x, y });
  };

  const openLightbox = () => setLightboxOpen(true);

  // Escape يغلق، الأسهم تتنقّل بين الصور، ونمنع تمرير الصفحة خلف الصندوق —
  // كلّه يعمل فقط حين الصندوق مفتوحاً (وتُستعاد حالة التمرير عند الإغلاق).
  //
  // المرحلة 16: والتركيز ينتقل إلى الصندوق عند فتحه ويعود إلى ما فتحه عند إغلاقه. بدونه كان
  // مستعمل لوحة المفاتيح يفتح عرضاً كاملاً وتركيزُه ما يزال في الصفحة تحته.
  const lightboxRef = useRef(null);
  useEffect(() => {
    if (!lightboxOpen) return undefined;
    const opener = document.activeElement;
    const onKeyDown = (e) => {
      if (e.key === 'Escape') setLightboxOpen(false);
      else if (e.key === 'ArrowRight') setActiveIndex((i) => (i + 1) % galleryImages.length);
      else if (e.key === 'ArrowLeft') setActiveIndex((i) => (i - 1 + galleryImages.length) % galleryImages.length);
    };
    window.addEventListener('keydown', onKeyDown);
    const prevOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    lightboxRef.current?.focus();
    return () => {
      window.removeEventListener('keydown', onKeyDown);
      document.body.style.overflow = prevOverflow;
      if (opener instanceof HTMLElement && document.contains(opener)) opener.focus();
    };
  }, [lightboxOpen, galleryImages.length]);

  return (
    <div className={styles.wrap}>
      {hasVideo && (
        <div className={styles.tabs} role="tablist">
          <button type="button" role="tab" aria-selected={activeTab === 'photo'}
            className={`${styles.tab} ${activeTab === 'photo' ? styles.tabActive : ''}`}
            onClick={() => setActiveTab('photo')}>
            {t('product.tabPhotos')}
          </button>
          <button type="button" role="tab" aria-selected={activeTab === 'video'}
            className={`${styles.tab} ${activeTab === 'video' ? styles.tabActive : ''}`}
            onClick={() => setActiveTab('video')}>
            {t('product.tabVideo')}
          </button>
        </div>
      )}

      {activeTab === 'video' && hasVideo ? (
        // لا مسار ترجمة: الفيديو يرفعه التاجر ولا تملك المنصّة نصّه. مسار فارغ يُعلن ترجمةً
        // غير موجودة لقارئ الشاشة — أسوأ من غيابه. رفعُ ملفّات ترجمة قدرةٌ ناقصة، لا وسمٌ ناقص.
        // eslint-disable-next-line jsx-a11y/media-has-caption
        <video className={styles.video} controls autoPlay={false} loop={false}
          poster={hasImages ? activeImage : undefined} src={videoUrl} />
      ) : (
        <>
          {/* صندوق الصورة كله قابل للنقر لفتح العرض الكامل (سطح مكتب وجوال معاً)
              — لا نعتمد على كشف اللمس الهشّ. تكبير التمرير مجرّد طبقة بصرية
              (pointer-events: none) فلا يعترض النقر. */}
          {/* الصندوق نفسه ليس عنصر تفاعل: التكبير بالتمرير طبقة بصرية، والنقر يخصّ الصورة.
              الصورة داخل زرّ حقيقي — فيفتح العرض الكامل بالفأرة وبمفتاح الإدخال معاً. */}
          <div
            ref={imageBoxRef}
            className={styles.mainBox}
            onMouseMove={!isTouchDevice ? handleMouseMove : undefined}
            onMouseEnter={!isTouchDevice ? () => setHovering(true) : undefined}
            onMouseLeave={!isTouchDevice ? () => setHovering(false) : undefined}
          >
            {hasImages ? (
              <button type="button" className={styles.imageButton} onClick={openLightbox}
                aria-label={t('product.openGallery', { name: productName })}>
                <img src={activeImage} alt={productName} className={styles.mainImg} />
              </button>
            ) : (
              <div className={styles.fallback} aria-hidden="true"><CameraIcon size={48} /></div>
            )}

            {hovering && hasImages && (
              <div
                className={styles.zoomOverlay}
                style={{
                  backgroundImage: `url(${activeImage})`,
                  backgroundPosition: `${cursorPct.x}% ${cursorPct.y}%`,
                  backgroundSize: `${zoomLevel * 100}%`,
                }}
                aria-hidden="true"
              />
            )}

            {hasImages && (
              <button type="button" className={styles.zoomIcon}
                onClick={openLightbox} aria-label={t('product.zoomAria')}>
                <SearchIcon size={16} />
              </button>
            )}
          </div>

          {hasImages && (
            <div className={styles.controls}>
              <button type="button" className={styles.zoomBtn}
                onClick={() => setZoomLevel((z) => clamp(z - ZOOM_STEP, MIN_ZOOM, MAX_ZOOM))}
                disabled={zoomLevel <= MIN_ZOOM} aria-label={t('product.zoomOut')}>
                🔍−
              </button>
              <span className={styles.zoomValue}>{zoomLevel}×</span>
              <button type="button" className={styles.zoomBtn}
                onClick={() => setZoomLevel((z) => clamp(z + ZOOM_STEP, MIN_ZOOM, MAX_ZOOM))}
                disabled={zoomLevel >= MAX_ZOOM} aria-label={t('product.zoomIn')}>
                🔍+
              </button>
              <button type="button" className={styles.fullscreenBtn} onClick={openLightbox}>
                <ExpandIcon size={15} /> {t('product.viewFullscreen')}
              </button>
            </div>
          )}

          {galleryImages.length > 1 && (
            <div className={styles.thumbs}>
              {galleryImages.slice(0, MAX_THUMBNAILS).map((img, i) => (
                <button key={img + i} type="button"
                  className={`${styles.thumb} ${i === activeIndex ? styles.thumbActive : ''}`}
                  onClick={() => setActiveIndex(i)}>
                  <img src={img} alt="" />
                </button>
              ))}
            </div>
          )}
        </>
      )}

      {/* العرض الكامل يُرسَم عبر Portal على body — خارج أي حاوية بـ overflow/
          transform قد تقصّه أو تحبس ترتيب طبقاته، فيبقى زر الإغلاق والخلفية فوق كل شيء.
          الخلفية زرّ شقيق للمسرح لا أبٌ له: النقر عليها يغلق بلا حاجة إلى إيقاف الانتشار من
          الصورة، وEscape يغلق أيضاً. */}
      {lightboxOpen && hasImages && createPortal(
        <div ref={lightboxRef} tabIndex={-1} className={styles.lightboxOverlay} role="dialog" aria-modal="true">
          <button type="button" className={styles.lightboxBackdrop}
            onClick={() => setLightboxOpen(false)} aria-label={t('common.close')} />
          <button type="button" className={styles.lightboxClose}
            onClick={() => setLightboxOpen(false)} aria-label={t('common.close')}>
            <CloseIcon size={22} />
          </button>

          <div className={styles.lightboxStage}>
            <img src={activeImage} alt={productName} className={styles.lightboxImg} />

            {galleryImages.length > 1 && (
              <>
                <button type="button" className={`${styles.lightboxNav} ${styles.navPrev}`}
                  onClick={() => setActiveIndex((i) => (i - 1 + galleryImages.length) % galleryImages.length)}
                  aria-label={t('common.previous')}>
                  <ChevronIcon dir="end" size={20} />
                </button>
                <button type="button" className={`${styles.lightboxNav} ${styles.navNext}`}
                  onClick={() => setActiveIndex((i) => (i + 1) % galleryImages.length)}
                  aria-label={t('common.next')}>
                  <ChevronIcon dir="start" size={20} />
                </button>
              </>
            )}
          </div>
        </div>,
        document.body
      )}
    </div>
  );
}
