import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import Drawer from '../../components/common/Drawer';
import FormField, { inputClass } from '../../components/common/FormField';
import Button from '../../components/common/Button';
import { ErrorBanner } from '../../components/common/StateViews';
import { CameraIcon } from '../../components/icons/Icons';
import { isRealImage } from '../../components/product/ProductImage';
import styles from './ProductFormDrawer.module.css';

const ALLOWED_TYPES = ['image/jpeg', 'image/png', 'image/webp', 'image/gif'];
const MAX_SIZE = 5 * 1024 * 1024;

// درج إضافة/تعديل منتج. رفع الصورة مؤجَّل فعلياً لبعد حفظ المنتج (POST
// products/{id}/image يتطلّب معرّفاً موجوداً) — هنا فقط نلتقط الملف ونعرض
// معاينته محلياً، والحفظ الفعلي يحدث في Products.jsx بعد الإنشاء/التحديث.
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
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);

  const pickFile = (f) => {
    if (!f) return;
    if (!ALLOWED_TYPES.includes(f.type)) return setError(t('admin.productForm.imageTypeError'));
    if (f.size > MAX_SIZE) return setError(t('admin.productForm.imageSizeError'));
    setError(null);
    setFile(f);
    setPreview(URL.createObjectURL(f));
  };

  const submit = async (e) => {
    e.preventDefault();
    if (!nameAr.trim()) return setError(t('admin.productForm.nameRequired'));
    if (!price || Number(price) <= 0) return setError(t('admin.productForm.priceInvalid'));
    if (!categoryId) return setError(t('admin.productForm.categoryRequired'));

    setBusy(true); setError(null);
    try {
      await onSave({
        nameAr: nameAr.trim(), nameEn: nameEn.trim() || null,
        description: description.trim(), price: Number(price),
        stockQuantity: Number(stockQuantity) || 0, categoryId: Number(categoryId),
        imageUrl: product?.imageUrl ?? '',
      }, file);
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
