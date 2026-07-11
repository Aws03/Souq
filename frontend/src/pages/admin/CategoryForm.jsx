import { useState } from 'react';
import Modal from '../../components/Modal';

const SLUG_PATTERN = /^[a-z0-9]+(?:-[a-z0-9]+)*$/;

export default function CategoryForm({ category, categories, onSave, onClose }) {
  const isEdit = !!category;
  const [name, setName] = useState(category?.name ?? '');
  const [slug, setSlug] = useState(category?.slug ?? '');
  const [parentId, setParentId] = useState(category?.parentId ?? '');
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);

  // فئة لا يمكن أن تكون أباً لنفسها، ولا لأحد أبنائها الحاليين (تبسيط: نمنع
  // فقط اختيار الفئة نفسها؛ الحلقات الأعمق يرفضها الخادم برسالة واضحة).
  const parentOptions = categories.filter((c) => c.id !== category?.id);

  const submit = async (e) => {
    e.preventDefault();
    if (!name.trim()) return setError('اسم الفئة مطلوب');
    if (!SLUG_PATTERN.test(slug.trim())) return setError('المُعرّف يقبل أحرفاً لاتينية صغيرة وأرقاماً وشرطات فقط، مثال: home-decor');

    setBusy(true);
    setError(null);
    try {
      await onSave({ name: name.trim(), slug: slug.trim(), parentId: parentId ? Number(parentId) : null });
    } catch (err) {
      setError(err.message);
      setBusy(false);
    }
  };

  return (
    <Modal title={isEdit ? 'تعديل فئة' : 'إضافة فئة'} onClose={onClose} busy={busy}>
      <form onSubmit={submit}>
        {error && <div className="auth-alert">⚠ {error}</div>}

        <div className="field">
          <label>اسم الفئة</label>
          <input value={name} onChange={(e) => setName(e.target.value)} placeholder="مثال: إلكترونيات" />
        </div>

        <div className="field">
          <label>المُعرّف (slug)</label>
          <input value={slug} onChange={(e) => setSlug(e.target.value.toLowerCase())} placeholder="electronics" dir="ltr" />
        </div>

        <div className="field">
          <label>الفئة الأب (اختياري)</label>
          <select value={parentId} onChange={(e) => setParentId(e.target.value)}>
            <option value="">بلا فئة أب</option>
            {parentOptions.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
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
