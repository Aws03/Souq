import { useEffect, useMemo, useRef, useState } from 'react';
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

// عارض صورة/فيديو منتج بتكبير يتتبّع المؤشّر (كعدسة صائغ خارجية) + أزرار
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

  // Escape يغلق، الأسهم تتنقّل بين الصور — يعمل فقط حين الصندوق مفتوحاً.
  useEffect(() => {
    if (!lightboxOpen) return;
    const onKeyDown = (e) => {
      if (e.key === 'Escape') setLightboxOpen(false);
      else if (e.key === 'ArrowRight') setActiveIndex((i) => (i + 1) % galleryImages.length);
      else if (e.key === 'ArrowLeft') setActiveIndex((i) => (i - 1 + galleryImages.length) % galleryImages.length);
    };
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
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
        <video className={styles.video} controls autoPlay={false} loop={false}
          poster={hasImages ? activeImage : undefined} src={videoUrl} />
      ) : (
        <>
          <div className={styles.mainRow}>
            <div
              ref={imageBoxRef}
              className={styles.mainBox}
              onMouseMove={!isTouchDevice ? handleMouseMove : undefined}
              onMouseEnter={!isTouchDevice ? () => setHovering(true) : undefined}
              onMouseLeave={!isTouchDevice ? () => setHovering(false) : undefined}
            >
              {hasImages ? (
                <img src={activeImage} alt={productName} className={styles.mainImg} />
              ) : (
                <div className={styles.fallback} aria-hidden="true"><CameraIcon size={48} /></div>
              )}

              {hasImages && (
                <button type="button" className={styles.zoomIcon}
                  onClick={openLightbox} aria-label={t('product.zoomAria')}>
                  <SearchIcon size={16} />
                </button>
              )}

              {isTouchDevice && hasImages && (
                <button type="button" className={styles.tapOverlay} onClick={openLightbox}
                  aria-label={t('product.zoomAria')} />
              )}
            </div>

            {hovering && hasImages && (
              <div
                className={styles.lens}
                style={{ insetInlineStart: `${cursorPct.x}%`, top: `${cursorPct.y}%` }}
                aria-hidden="true"
              />
            )}

            {hovering && hasImages && (
              <div
                className={styles.zoomPanel}
                style={{
                  backgroundImage: `url(${activeImage})`,
                  backgroundPosition: `${cursorPct.x}% ${cursorPct.y}%`,
                  backgroundSize: `${zoomLevel * 100}%`,
                }}
                aria-hidden="true"
              />
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

      {lightboxOpen && hasImages && (
        <div className={styles.lightboxOverlay} onClick={() => setLightboxOpen(false)}>
          <button type="button" className={styles.lightboxClose}
            onClick={() => setLightboxOpen(false)} aria-label={t('common.close')}>
            <CloseIcon size={22} />
          </button>

          <div className={styles.lightboxStage} onClick={(e) => e.stopPropagation()}>
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
        </div>
      )}
    </div>
  );
}
