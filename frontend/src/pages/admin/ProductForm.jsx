import { useState } from 'react';
import Modal from '../../components/Modal';

const ALLOWED_TYPES = ['image/jpeg', 'image/png', 'image/webp', 'image/gif'];
const MAX_SIZE = 5 * 1024 * 1024;

// نموذج إضافة/تعديل منتج داخل نافذة منبثقة. رفع الصورة مؤجَّل فعلياً لبعد حفظ
// المنتج (POST products/{id}/image يتطلّب معرّفاً موجوداً) — هنا فقط نلتقط
// الملف ونعرض معاينته محلياً، والحفظ الفعلي يحدث في Products.jsx بعد الإنشاء/التحديث.
export default function ProductForm({ product, categories, onSave, onClose }) {
  const isEdit = !!product;
  const [name, setName] = useState(product?.name ?? '');
  const [description, setDescription] = useState(product?.description ?? '');
  const [price, setPrice] = useState(product?.price ?? '');
  const [stockQuantity, setStockQuantity] = useState(product?.stockQuantity ?? '');
  const [categoryId, setCategoryId] = useState(product?.categoryId ?? (categories[0]?.id ?? ''));
  const [file, setFile] = useState(null);
  const [preview, setPreview] = useState(product?.imageUrl || null);
  const [dragOver, setDragOver] = useState(false);
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);

  const pickFile = (f) => {
    if (!f) return;
    if (!ALLOWED_TYPES.includes(f.type)) {
      setError('صيغة الصورة غير مدعومة (JPEG/PNG/WebP/GIF فقط)');
      return;
    }
    if (f.size > MAX_SIZE) {
      setError('حجم الصورة يتجاوز 5 ميغابايت');
      return;
    }
    setError(null);
    setFile(f);
    setPreview(URL.createObjectURL(f));
  };

  const submit = async (e) => {
    e.preventDefault();
    if (!name.trim()) return setError('اسم المنتج مطلوب');
    if (!price || Number(price) <= 0) return setError('السعر يجب أن يكون أكبر من صفر');
    if (!categoryId) return setError('اختر فئة للمنتج');

    setBusy(true);
    setError(null);
    try {
      await onSave({
        name: name.trim(),
        description: description.trim(),
        price: Number(price),
        stockQuantity: Number(stockQuantity) || 0,
        categoryId: Number(categoryId),
        imageUrl: product?.imageUrl ?? '',
      }, file);
    } catch (err) {
      setError(err.message);
      setBusy(false);
    }
  };

  return (
    <Modal title={isEdit ? 'تعديل منتج' : 'إضافة منتج'} onClose={onClose} busy={busy}>
      <form onSubmit={submit}>
        {error && <div className="auth-alert">⚠ {error}</div>}

        <div
          className={'dropzone' + (dragOver ? ' drag-over' : '')}
          onDragOver={(e) => { e.preventDefault(); setDragOver(true); }}
          onDragLeave={() => setDragOver(false)}
          onDrop={(e) => { e.preventDefault(); setDragOver(false); pickFile(e.dataTransfer.files?.[0]); }}
          onClick={() => document.getElementById('product-image-input').click()}
        >
          {preview
            ? <img src={preview} alt="معاينة" className="dropzone-preview" />
            : <div className="dropzone-hint">اسحب صورة هنا أو انقر للاختيار<br /><small>JPEG · PNG · WebP · GIF — حتى 5 ميغابايت</small></div>}
          <input id="product-image-input" type="file" accept="image/jpeg,image/png,image/webp,image/gif"
            hidden onChange={(e) => pickFile(e.target.files?.[0])} />
        </div>

        <div className="field">
          <label>اسم المنتج</label>
          <input value={name} onChange={(e) => setName(e.target.value)} placeholder="مثال: سماعات لاسلكية" />
        </div>

        <div className="field">
          <label>الوصف</label>
          <textarea rows={3} value={description} onChange={(e) => setDescription(e.target.value)} />
        </div>

        <div className="field-row">
          <div className="field">
            <label>السعر (ر.س)</label>
            <input type="number" min="0" step="0.01" value={price} onChange={(e) => setPrice(e.target.value)} />
          </div>
          <div className="field">
            <label>المخزون</label>
            <input type="number" min="0" step="1" value={stockQuantity} onChange={(e) => setStockQuantity(e.target.value)} />
          </div>
        </div>

        <div className="field">
          <label>الفئة</label>
          <select value={categoryId} onChange={(e) => setCategoryId(e.target.value)}>
            {categories.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
          </select>
        </div>

        <div className="modal-foot">
          <button type="button" className="btn-ghost" onClick={onClose} disabled={busy}>إلغاء</button>
          <button type="submit" className="btn-primary" disabled={busy}>{busy ? 'جارٍ الحفظ...' : 'حفظ'}</button>
        </div>
      </form>
    </Modal>
  );
}
