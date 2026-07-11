import { useState, useEffect, useCallback } from 'react';
import { api } from '../../api/client';
import Pagination from '../../components/Pagination';
import ProductForm from './ProductForm';

const PAGE_SIZE = 10;

// شاشة إدارة المنتجات: جدول ببحث/تصفية/ترقيم حقيقية من الـ API + نموذج
// إضافة/تعديل بسحب وإفلات صورة. الحذف حذف منطقي (تعطيل) — يختفي المنتج من
// القائمة فوراً لأن GET /products يعرض النشط فقط (سلوك موثّق منذ المرحلة 1).
export default function Products() {
  const [items, setItems] = useState([]);
  const [totalPages, setTotalPages] = useState(1);
  const [page, setPage] = useState(1);
  const [keyword, setKeyword] = useState('');
  const [categoryId, setCategoryId] = useState('');
  const [categories, setCategories] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [editing, setEditing] = useState(null); // null=مغلق، {}=إضافة، منتج=تعديل

  useEffect(() => { api.getCategories().then(setCategories).catch(() => {}); }, []);

  const load = useCallback(() => {
    setLoading(true);
    api.getProducts({ keyword: keyword || undefined, categoryId: categoryId || undefined, page, pageSize: PAGE_SIZE })
      .then((res) => { setItems(res.items); setTotalPages(res.totalPages); setError(null); })
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
  }, [keyword, categoryId, page]);

  useEffect(() => { load(); }, [load]);

  // البحث/التصفية تُرجع دائماً للصفحة الأولى — نتيجة مختلفة عن الصفحة الحالية.
  useEffect(() => { setPage(1); }, [keyword, categoryId]);

  const save = async (payload, file) => {
    const id = editing?.id
      ? (await api.updateProduct(editing.id, payload), editing.id)
      : (await api.createProduct(payload)).id;
    if (file) await api.uploadProductImage(id, file);
    setEditing(null);
    load();
  };

  const remove = async (product) => {
    if (!window.confirm(`تعطيل المنتج "${product.name}"؟ سيختفي من المتجر.`)) return;
    try {
      await api.deleteProduct(product.id);
      load();
    } catch (e) {
      setError(e.message);
    }
  };

  return (
    <div>
      <h2 className="admin-page-title">إدارة المنتجات</h2>
      <p className="admin-page-sub">أضف منتجات جديدة، عدّل بياناتها وصورها، أو عطّلها.</p>

      {error && <div className="auth-alert">⚠ {error}</div>}

      <div className="admin-toolbar">
        <input className="search" placeholder="ابحث بالاسم أو الوصف..."
          value={keyword} onChange={(e) => setKeyword(e.target.value)} />
        <select value={categoryId} onChange={(e) => setCategoryId(e.target.value)}>
          <option value="">كل الفئات</option>
          {categories.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
        </select>
        <button className="btn-primary" onClick={() => setEditing({})}>+ إضافة منتج</button>
      </div>

      <div className="admin-table-wrap">
        <table className="admin-table">
          <thead>
            <tr>
              <th>الصورة</th><th>الاسم</th><th>الفئة</th><th>السعر</th><th>المخزون</th><th></th>
            </tr>
          </thead>
          <tbody>
            {loading && <tr><td colSpan={6} className="admin-table-empty">جارٍ التحميل...</td></tr>}
            {!loading && items.length === 0 && <tr><td colSpan={6} className="admin-table-empty">لا منتجات مطابقة</td></tr>}
            {!loading && items.map((p) => (
              <tr key={p.id}>
                <td><div className="table-thumb">{p.imageUrl ? <img src={p.imageUrl} alt={p.name} /> : '—'}</div></td>
                <td>{p.name}</td>
                <td>{p.categoryName || '—'}</td>
                <td>{p.price.toFixed(2)} {p.currency}</td>
                <td>{p.stockQuantity <= 3 ? <span className="stock-low">{p.stockQuantity}</span> : p.stockQuantity}</td>
                <td className="admin-table-actions">
                  <button className="btn-ghost" onClick={() => setEditing(p)}>تعديل</button>
                  <button className="btn-danger" onClick={() => remove(p)}>تعطيل</button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <Pagination page={page} totalPages={totalPages} onChange={setPage} />

      {editing !== null && (
        <ProductForm product={editing.id ? editing : null} categories={categories}
          onSave={save} onClose={() => setEditing(null)} />
      )}
    </div>
  );
}
