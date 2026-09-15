import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api } from '../../api/client';
import Drawer from '../../components/common/Drawer';
import FormField, { inputClass } from '../../components/common/FormField';
import Button from '../../components/common/Button';
import { ErrorBanner } from '../../components/common/StateViews';
import { CameraIcon, VideoIcon, CloseIcon } from '../../components/icons/Icons';
import { getCategoryName } from '../../components/product/ProductBadges';
import { hasAnyName, textsToForm } from '../../features/catalog/catalogText';
import { buildProductPayload } from '../../features/admin/products/productPayload';
import styles from './ProductFormDrawer.module.css';

const ALLOWED_IMAGE_TYPES = ['image/jpeg', 'image/png', 'image/webp', 'image/gif'];
const MAX_IMAGE_SIZE = 5 * 1024 * 1024;
const ALLOWED_VIDEO_TYPES = ['video/mp4', 'video/webm'];
const MAX_VIDEO_SIZE = 50 * 1024 * 1024;
const MAX_IMAGES = 10;
const SLUG_PATTERN = /^[a-z0-9]+(?:-[a-z0-9]+)*$/;

// درج إضافة/تعديل منتج (المرحلة 5): نصوص لكل لغة، السعر وسعر المقارنة وSKU (المتغيّر الافتراضي)، المعرّف في
// الرابط، ومعرض الصور. الصورة الجديدة والفيديو يُرفعان بعد حفظ المنتج (نقاط الرفع تتطلّب معرّفاً موجوداً) — هنا
// نلتقطهما ونعرض معاينتهما. إزالة صورة موجودة أو جعلها رئيسية عملية مستقلّة على المعرض تُنفَّذ فوراً.
export default function ProductFormDrawer({ product, categories, onSave, onImagesChanged, onClose }) {
  const { t } = useTranslation();
  const isEdit = !!product;
  const [texts, setTexts] = useState(() => textsToForm(product?.translations));
  const [slug, setSlug] = useState(product?.slug ?? '');
  const [sku, setSku] = useState(product?.sku ?? '');
  const [brand, setBrand] = useState(product?.brand ?? '');
  const [price, setPrice] = useState(product?.price ?? '');
  const [compareAtPrice, setCompareAtPrice] = useState(product?.compareAtPrice ?? '');
  // يفتحان مخزون المنتج عند الإنشاء فقط؛ بعده التصحيح من صفحة الجرد (المرحلة 6).
  const [stockQuantity, setStockQuantity] = useState('');
  const [lowStockThreshold, setLowStockThreshold] = useState(5);
  const [categoryId, setCategoryId] = useState(product?.categoryId ?? (categories[0]?.id ?? ''));
  const [status, setStatus] = useState('Active');
  const [images, setImages] = useState(product?.images ?? []);
  const [file, setFile] = useState(null);
  const [preview, setPreview] = useState(null);
  const [dragOver, setDragOver] = useState(false);
  const [videoFile, setVideoFile] = useState(null);
  const [videoPreview, setVideoPreview] = useState(product?.videoUrl || null);
  const [videoRemoved, setVideoRemoved] = useState(false);
  const [videoDragOver, setVideoDragOver] = useState(false);
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);

  const galleryFull = images.length >= MAX_IMAGES;
  const setText = (culture, field) => (e) =>
    setTexts((current) => ({ ...current, [culture]: { ...current[culture], [field]: e.target.value } }));

  const pickFile = (f) => {
    if (!f) return;
    if (!ALLOWED_IMAGE_TYPES.includes(f.type)) return setError(t('admin.productForm.imageTypeError'));
    if (f.size > MAX_IMAGE_SIZE) return setError(t('admin.productForm.imageSizeError'));
    setError(null);
    setFile(f);
    setPreview(URL.createObjectURL(f));
  };

  const pickVideo = (f) => {
    if (!f) return;
    if (!ALLOWED_VIDEO_TYPES.includes(f.type)) return setError(t('admin.productForm.videoTypeError'));
    if (f.size > MAX_VIDEO_SIZE) return setError(t('admin.productForm.videoSizeError'));
    setError(null);
    setVideoFile(f);
    setVideoRemoved(false);
    setVideoPreview(URL.createObjectURL(f));
  };

  const removeVideo = (e) => {
    e.stopPropagation();
    setVideoFile(null);
    setVideoPreview(null);
    setVideoRemoved(true);
  };

  const runGallery = async (action) => {
    setBusy(true); setError(null);
    try { await action(); onImagesChanged?.(); } catch (err) { setError(err.message); } finally { setBusy(false); }
  };

  const removeImage = (image) => {
    if (!window.confirm(t('admin.productForm.confirmRemoveImage'))) return;
    runGallery(async () => {
      await api.removeProductImage(product.id, image.id);
      setImages((list) => list.filter((i) => i.id !== image.id));
    });
  };

  // الترتيب الجديد يشمل كل الصور (عقد الخادم): المختارة أولاً ثم البقية كما هي.
  const makePrimary = (image) => runGallery(async () => {
    const ordered = [image, ...images.filter((i) => i.id !== image.id)];
    await api.reorderProductImages(product.id, ordered.map((i) => i.id));
    setImages(ordered);
  });

  const submit = async (e) => {
    e.preventDefault();
    if (!hasAnyName(texts)) return setError(t('admin.productForm.nameRequired'));
    if (!price || Number(price) <= 0) return setError(t('admin.productForm.priceInvalid'));
    if (compareAtPrice !== '' && compareAtPrice !== null && Number(compareAtPrice) <= Number(price)) {
      return setError(t('admin.productForm.compareAtInvalid'));
    }
    if (!categoryId) return setError(t('admin.productForm.categoryRequired'));
    if (slug.trim() && !SLUG_PATTERN.test(slug.trim())) return setError(t('admin.productForm.slugInvalid'));

    setBusy(true); setError(null);
    try {
      // المخزون يُرسَل فقط إن غيّره المدير (مع القيمة التي رآها) — تعارض ⇒ 409 تظهر رسالته هنا.
      await onSave(buildProductPayload({
        texts, slug, sku, brand, price, compareAtPrice, stockQuantity, lowStockThreshold, categoryId, status, videoRemoved,
      }, product), galleryFull ? null : file, videoFile);
    } catch (err) { setError(err.message); setBusy(false); }
  };

  return (
    <Drawer open onClose={onClose} side="right" busy={busy} title={isEdit ? t('admin.productForm.editTitle') : t('admin.productForm.addTitle')}
      footer={
        <div className={styles.footActions}>
          <Button variant="ghost" onClick={onClose} disabled={busy}>{t('common.cancel')}</Button>
          <Button variant="primary" type="submit" form="product-form" loading={busy}>{t('common.save')}</Button>
        </div>
      }>
      <form id="product-form" onSubmit={submit}>
        {error && <ErrorBanner message={error} />}

        {images.length > 0 && (
          <section className={styles.galleryBlock} aria-label={t('admin.productForm.galleryLabel')}>
            <div className={styles.galleryTitle}>{t('admin.productForm.galleryLabel')}</div>
            <small className={styles.galleryHint}>{t('admin.productForm.galleryHint', { max: MAX_IMAGES })}</small>
            <ul className={styles.gallery}>
              {images.map((image, index) => (
                <li key={image.id} className={styles.galleryItem}>
                  <img src={image.url} alt="" className={styles.galleryImage} />
                  <div className={styles.galleryActions}>
                    {index === 0
                      ? <span className={styles.primaryTag}>{t('admin.productForm.primaryImage')}</span>
                      : <button type="button" onClick={() => makePrimary(image)} disabled={busy}>{t('admin.productForm.makePrimary')}</button>}
                    <button type="button" className={styles.removeImageBtn} onClick={() => removeImage(image)} disabled={busy}>
                      {t('admin.productForm.removeImage')}
                    </button>
                  </div>
                </li>
              ))}
            </ul>
          </section>
        )}

        {/* منطقة الإفلات كانت <div> بمستمع نقر يضغط حقلاً مخفياً: لا يصلها Tab ولا يعرفها قارئ
            الشاشة. الآن <label> فوق حقل ملفّ حقيقي مخفيّ بصرياً لا عن المتصفّح — النقر واللمس
            ولوحة المفاتيح كلها تفتح منتقي الملفّات، والإفلات يبقى كما هو. */}
        {galleryFull ? <p className={styles.galleryFull}>{t('admin.productForm.galleryFull')}</p> : (
          <div className={`${styles.dropzone} ${dragOver ? styles.dragOver : ''}`}
            onDragOver={(e) => { e.preventDefault(); setDragOver(true); }}
            onDragLeave={() => setDragOver(false)}
            onDrop={(e) => { e.preventDefault(); setDragOver(false); pickFile(e.dataTransfer.files?.[0]); }}>
            <label htmlFor="product-image-input" className={styles.dropLabel}>
              {preview ? <img src={preview} alt={t('admin.productForm.previewAlt')} className={styles.preview} /> : (
                <div className={styles.dropHint}>
                  <CameraIcon />
                  <span>{t('admin.productForm.dropHint')}</span>
                  <small>{t('admin.productForm.dropHintSub')}</small>
                </div>
              )}
            </label>
            <input id="product-image-input" type="file" accept="image/jpeg,image/png,image/webp,image/gif"
              className="souq-visually-hidden" onChange={(e) => pickFile(e.target.files?.[0])} />
          </div>
        )}

        <FormField label={t('admin.productForm.videoLabel')}>
          <div className={`${styles.dropzone} ${videoDragOver ? styles.dragOver : ''}`}
            onDragOver={(e) => { e.preventDefault(); setVideoDragOver(true); }}
            onDragLeave={() => setVideoDragOver(false)}
            onDrop={(e) => { e.preventDefault(); setVideoDragOver(false); pickVideo(e.dataTransfer.files?.[0]); }}>
            {videoPreview ? (
              // المعاينة ليست منطقة اختيار: الفيديو له مشغّله، والاستبدال يمرّ بالحذف أوّلاً.
              <div className={styles.videoPreviewWrap}>
                {/* لا مسار ترجمة: الملفّ يرفعه التاجر ولا تملك المنصّة نصّه (كما في ProductZoom). */}
                {/* eslint-disable-next-line jsx-a11y/media-has-caption */}
                <video src={videoPreview} className={styles.videoPreview} controls />
                <button type="button" className={styles.removeVideoBtn} onClick={removeVideo}>
                  <CloseIcon size={14} /> {t('admin.productForm.removeVideo')}
                </button>
              </div>
            ) : (
              <label htmlFor="product-video-input" className={styles.dropLabel}>
                <div className={styles.dropHint}>
                  <VideoIcon />
                  <span>{t('admin.productForm.videoDropHint')}</span>
                  <small>{t('admin.productForm.videoDropHintSub')}</small>
                </div>
              </label>
            )}
            <input id="product-video-input" type="file" accept="video/mp4,video/webm"
              className="souq-visually-hidden" onChange={(e) => pickVideo(e.target.files?.[0])} />
          </div>
        </FormField>

        <div className={styles.row}>
          <FormField label={t('admin.productForm.nameLabel')}>
            <input className={inputClass(false)} value={texts.ar.name} dir="rtl" maxLength={200}
              onChange={setText('ar', 'name')} placeholder="مثال: سماعات لاسلكية" />
          </FormField>
          <FormField label={t('admin.productForm.nameEnLabel')} hint={t('admin.productForm.nameEnHint')}>
            <input className={inputClass(false)} value={texts.en.name} dir="ltr" maxLength={200}
              onChange={setText('en', 'name')} placeholder="e.g. Wireless Headphones" />
          </FormField>
        </div>

        <FormField label={t('admin.productForm.descriptionLabel')}>
          <textarea className={inputClass(false)} rows={3} dir="rtl" maxLength={4000}
            value={texts.ar.description} onChange={setText('ar', 'description')} />
        </FormField>
        <FormField label={t('admin.productForm.descriptionEnLabel')}>
          <textarea className={inputClass(false)} rows={3} dir="ltr" maxLength={4000}
            value={texts.en.description} onChange={setText('en', 'description')} />
        </FormField>

        <div className={styles.row}>
          <FormField label={t('admin.productForm.priceLabel')}>
            <input className={inputClass(false)} type="number" min="0" step="0.001" value={price} onChange={(e) => setPrice(e.target.value)} />
          </FormField>
          <FormField label={t('admin.productForm.compareAtLabel')} hint={t('admin.productForm.compareAtHint')}>
            <input className={inputClass(false)} type="number" min="0" step="0.001" value={compareAtPrice}
              onChange={(e) => setCompareAtPrice(e.target.value)} />
          </FormField>
        </div>

        {isEdit ? (
          <div className={styles.stockSummary}>
            <div>{t('admin.productForm.stockSummary', { onHand: product.onHand ?? 0, reserved: product.reserved ?? 0, available: product.available ?? 0 })}</div>
            <small>{t('admin.productForm.stockManagedInInventory')}</small>
          </div>
        ) : (
          <div className={styles.row}>
            <FormField label={t('admin.productForm.stockLabel')}>
              <input className={inputClass(false)} type="number" min="0" step="1" value={stockQuantity} onChange={(e) => setStockQuantity(e.target.value)} />
            </FormField>
            <FormField label={t('admin.productForm.lowStockLabel')}>
              <input className={inputClass(false)} type="number" min="0" step="1" value={lowStockThreshold}
                onChange={(e) => setLowStockThreshold(e.target.value)} />
            </FormField>
          </div>
        )}

        <div className={styles.row}>
          <FormField label={t('admin.productForm.categoryLabel')}>
            <select className={inputClass(false)} value={categoryId} onChange={(e) => setCategoryId(e.target.value)}>
              {categories.map((c) => <option key={c.id} value={c.id}>{getCategoryName(c)}</option>)}
            </select>
          </FormField>
          <FormField label={t('admin.productForm.brandLabel')}>
            <input className={inputClass(false)} value={brand} maxLength={100} onChange={(e) => setBrand(e.target.value)} />
          </FormField>
        </div>

        <div className={styles.row}>
          <FormField label={t('admin.productForm.skuLabel')}>
            <input className={inputClass(false)} value={sku} dir="ltr" maxLength={64} placeholder="HP-01"
              onChange={(e) => setSku(e.target.value)} />
          </FormField>
          <FormField label={t('admin.productForm.slugLabel')} hint={isEdit ? undefined : t('admin.productForm.slugHint')}>
            <input className={inputClass(false)} value={slug} dir="ltr" maxLength={120} placeholder="wireless-headphones"
              onChange={(e) => setSlug(e.target.value.toLowerCase())} />
          </FormField>
        </div>

        {!isEdit && (
          <FormField label={t('admin.productForm.statusLabel')}>
            <select className={inputClass(false)} value={status} onChange={(e) => setStatus(e.target.value)}>
              <option value="Active">{t('admin.products.status.Active')}</option>
              <option value="Draft">{t('admin.products.status.Draft')}</option>
            </select>
          </FormField>
        )}
      </form>
    </Drawer>
  );
}
