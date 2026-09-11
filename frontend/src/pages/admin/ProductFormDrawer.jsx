import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import Drawer from '../../components/common/Drawer';
import FormField, { inputClass } from '../../components/common/FormField';
import Button from '../../components/common/Button';
import { ErrorBanner } from '../../components/common/StateViews';
import { CameraIcon, VideoIcon, CloseIcon } from '../../components/icons/Icons';
import { isRealImage } from '../../components/product/ProductImage';
import { buildProductPayload } from '../../features/admin/products/productPayload';
import styles from './ProductFormDrawer.module.css';

const ALLOWED_IMAGE_TYPES = ['image/jpeg', 'image/png', 'image/webp', 'image/gif'];
const MAX_IMAGE_SIZE = 5 * 1024 * 1024;
const ALLOWED_VIDEO_TYPES = ['video/mp4', 'video/webm'];
const MAX_VIDEO_SIZE = 50 * 1024 * 1024;

// درج إضافة/تعديل منتج. رفع الصورة/الفيديو مؤجَّل فعلياً لبعد حفظ المنتج (نقاط
// الرفع تتطلّب معرّفاً موجوداً) — هنا فقط نلتقط الملفّين ونعرض معاينتهما محلياً،
// والحفظ الفعلي يحدث في Products.jsx بعد الإنشاء/التحديث.
export default function ProductFormDrawer({ product, categories, onSave, onClose }) {
  const { t } = useTranslation();
  const isEdit = !!product;
  const [nameAr, setNameAr] = useState(product?.nameAr ?? '');
  const [nameEn, setNameEn] = useState(product?.nameEn ?? '');
  const [description, setDescription] = useState(product?.description ?? '');
  const [price, setPrice] = useState(product?.price ?? '');
  const [stockQuantity, setStockQuantity] = useState(product?.stockQuantity ?? '');
  const [categoryId, setCategoryId] = useState(product?.categoryId ?? (categories[0]?.id ?? ''));
  const [file, setFile] = useState(null);
  const [preview, setPreview] = useState(isRealImage(product?.imageUrl) ? product.imageUrl : null);
  const [dragOver, setDragOver] = useState(false);
  const [videoFile, setVideoFile] = useState(null);
  const [videoPreview, setVideoPreview] = useState(product?.videoUrl || null);
  const [videoRemoved, setVideoRemoved] = useState(false);
  const [videoDragOver, setVideoDragOver] = useState(false);
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);

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

  const submit = async (e) => {
    e.preventDefault();
    if (!nameAr.trim()) return setError(t('admin.productForm.nameRequired'));
    if (!price || Number(price) <= 0) return setError(t('admin.productForm.priceInvalid'));
    if (!categoryId) return setError(t('admin.productForm.categoryRequired'));

    setBusy(true); setError(null);
    try {
      // المخزون يُرسَل فقط إن غيّره المدير (مع القيمة التي رآها) — تعارض ⇒ 409 تظهر رسالته هنا.
      await onSave(buildProductPayload(
        { nameAr, nameEn, description, price, stockQuantity, categoryId, videoRemoved }, product,
      ), file, videoFile);
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

        <div className={`${styles.dropzone} ${dragOver ? styles.dragOver : ''}`}
          onDragOver={(e) => { e.preventDefault(); setDragOver(true); }}
          onDragLeave={() => setDragOver(false)}
          onDrop={(e) => { e.preventDefault(); setDragOver(false); pickFile(e.dataTransfer.files?.[0]); }}
          onClick={() => document.getElementById('product-image-input').click()}>
          {preview ? <img src={preview} alt={t('admin.productForm.previewAlt')} className={styles.preview} /> : (
            <div className={styles.dropHint}>
              <CameraIcon />
              <span>{t('admin.productForm.dropHint')}</span>
              <small>{t('admin.productForm.dropHintSub')}</small>
            </div>
          )}
          <input id="product-image-input" type="file" accept="image/jpeg,image/png,image/webp,image/gif"
            hidden onChange={(e) => pickFile(e.target.files?.[0])} />
        </div>

        <FormField label={t('admin.productForm.videoLabel')}>
          <div className={`${styles.dropzone} ${videoDragOver ? styles.dragOver : ''}`}
            onDragOver={(e) => { e.preventDefault(); setVideoDragOver(true); }}
            onDragLeave={() => setVideoDragOver(false)}
            onDrop={(e) => { e.preventDefault(); setVideoDragOver(false); pickVideo(e.dataTransfer.files?.[0]); }}
            onClick={() => document.getElementById('product-video-input').click()}>
            {videoPreview ? (
              <div className={styles.videoPreviewWrap}>
                <video src={videoPreview} className={styles.videoPreview} controls onClick={(e) => e.stopPropagation()} />
                <button type="button" className={styles.removeVideoBtn} onClick={removeVideo}>
                  <CloseIcon size={14} /> {t('admin.productForm.removeVideo')}
                </button>
              </div>
            ) : (
              <div className={styles.dropHint}>
                <VideoIcon />
                <span>{t('admin.productForm.videoDropHint')}</span>
                <small>{t('admin.productForm.videoDropHintSub')}</small>
              </div>
            )}
            <input id="product-video-input" type="file" accept="video/mp4,video/webm"
              hidden onChange={(e) => pickVideo(e.target.files?.[0])} />
          </div>
        </FormField>

        <div className={styles.row}>
          <FormField label={t('admin.productForm.nameLabel')}>
            <input className={inputClass(false)} value={nameAr} dir="rtl"
              onChange={(e) => setNameAr(e.target.value)} placeholder="مثال: سماعات لاسلكية" />
          </FormField>
          <FormField label={t('admin.productForm.nameEnLabel')} hint={t('admin.productForm.nameEnHint')}>
            <input className={inputClass(false)} value={nameEn} dir="ltr"
              onChange={(e) => setNameEn(e.target.value)} placeholder="e.g. Wireless Headphones" />
          </FormField>
        </div>

        <FormField label={t('admin.productForm.descriptionLabel')}>
          <textarea className={inputClass(false)} rows={3} value={description} onChange={(e) => setDescription(e.target.value)} />
        </FormField>

        <div className={styles.row}>
          <FormField label={t('admin.productForm.priceLabel')}>
            <input className={inputClass(false)} type="number" min="0" step="0.001" value={price} onChange={(e) => setPrice(e.target.value)} />
          </FormField>
          <FormField label={t('admin.productForm.stockLabel')}>
            <input className={inputClass(false)} type="number" min="0" step="1" value={stockQuantity} onChange={(e) => setStockQuantity(e.target.value)} />
          </FormField>
        </div>

        <FormField label={t('admin.productForm.categoryLabel')}>
          <select className={inputClass(false)} value={categoryId} onChange={(e) => setCategoryId(e.target.value)}>
            {categories.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
          </select>
        </FormField>
      </form>
    </Drawer>
  );
}
