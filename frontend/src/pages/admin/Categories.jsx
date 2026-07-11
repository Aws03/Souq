import { useState, useEffect, useCallback } from 'react';
import { api } from '../../api/client';
import CategoryForm from './CategoryForm';

// شاشة إدارة الفئات. القائمة صغيرة عادةً (غير مرقّمة في الـ API) فنعرضها كاملة
// كجدول مسطّح، مع اسم الفئة الأب محلولاً من القائمة نفسها.
export default function Categories() {
  const [categories, setCategories] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [editing, setEditing] = useState(null);

  const load = useCallback(() => {
    setLoading(true);
    api.getCategories()
      .then((res) => { setCategories(res); setError(null); })
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
  }, []);

  useEffect(() => { load(); }, [load]);

  const parentName = (id) => categories.find((c) => c.id === id)?.name || '—';

  const save = async (payload) => {
    if (editing?.id) await api.updateCategory(editing.id, payload);
    else await api.createCategory(payload);
    setEditing(null);
    load();
  };

  const remove = async (category) => {
    if (!window.confirm(`حذف الفئة "${category.name}"؟`)) return;
    try {
      await api.deleteCategory(category.id);
      load();
    } catch (e) {
      setError(e.message);
    }
  };

  return (
    <div>
      <h2 className="admin-page-title">إدارة الفئات</h2>
      <p className="admin-page-sub">نظّم فئات المتجر وفئاتها الفرعية.</p>

      {error && <div className="auth-alert">⚠ {error}</div>}

      <div className="admin-toolbar">
        <button className="btn-primary" onClick={() => setEditing({})}>+ إضافة فئة</button>
      </div>

      <div className="admin-table-wrap">
        <table className="admin-table">
          <thead>
            <tr><th>الاسم</th><th>المُعرّف</th><th>الفئة الأب</th><th></th></tr>
          </thead>
          <tbody>
            {loading && <tr><td colSpan={4} className="admin-table-empty">جارٍ التحميل...</td></tr>}
            {!loading && categories.length === 0 && <tr><td colSpan={4} className="admin-table-empty">لا فئات بعد</td></tr>}
            {!loading && categories.map((c) => (
              <tr key={c.id}>
                <td>{c.name}</td>
                <td dir="ltr" style={{ textAlign: 'right' }}>{c.slug}</td>
                <td>{c.parentId ? parentName(c.parentId) : '—'}</td>
                <td className="admin-table-actions">
                  <button className="btn-ghost" onClick={() => setEditing(c)}>تعديل</button>
                  <button className="btn-danger" onClick={() => remove(c)}>حذف</button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {editing !== null && (
        <CategoryForm category={editing.id ? editing : null} categories={categories}
          onSave={save} onClose={() => setEditing(null)} />
      )}
    </div>
  );
}
