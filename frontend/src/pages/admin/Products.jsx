import { useState, useEffect, useCallback } from 'react';
import { api } from '../../api/client';
import { useToast } from '../../context/ToastContext';
import DataTable from '../../components/common/DataTable';
import RowActionsMenu from '../../components/common/RowActionsMenu';
import Pagination from '../../components/common/Pagination';
import Button from '../../components/common/Button';
import ProductImage from '../../components/product/ProductImage';
import { formatPrice } from '../../components/product/ProductBadges';
import { SearchIcon } from '../../components/icons/Icons';
import ProductFormDrawer from './ProductFormDrawer';
import styles from './Admin.module.css';

const PAGE_SIZE = 10;

// شاشة إدارة المنتجات: جدول ببحث/تصفية/ترقيم حقيقية من الـ API + درج
// إضافة/تعديل بسحب وإفلات صورة. الحذف حذف منطقي (تعطيل) — يختفي المنتج من
// القائمة فوراً لأن GET /products يعرض النشط فقط.
export default function Products() {
  const toast = useToast();
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
  useEffect(() => { setPage(1); }, [keyword, categoryId]);

  const save = async (payload, file) => {
    const id = editing?.id
      ? (await api.updateProduct(editing.id, payload), editing.id)
      : (await api.createProduct(payload)).id;
    if (file) await api.uploadProductImage(id, file);
    setEditing(null);
    toast.success(editing?.id ? 'تم تحديث المنتج' : 'تمت إضافة المنتج');
    load();
  };

  const remove = async (product) => {
    if (!window.confirm(`تعطيل المنتج "${product.name}"؟ سيختفي من المتجر.`)) return;
    try {
      await api.deleteProduct(product.id);
      toast.success('تم تعطيل المنتج');
      load();
    } catch (e) { toast.error(e.message); }
  };

  const columns = [
    { key: 'img', header: 'الصورة', render: (p) => <div className={styles.thumb}><ProductImage product={p} /></div> },
    { key: 'name', header: 'الاسم', render: (p) => p.name },
    { key: 'cat', header: 'الفئة', render: (p) => p.categoryName || '—' },
    { key: 'price', header: 'السعر', render: (p) => formatPrice(p.price, p.currency) },
    { key: 'stock', header: 'المخزون', render: (p) => p.stockQuantity },
    {
      key: 'actions', header: '', render: (p) => (
        <RowActionsMenu actions={[
          { label: 'تعديل', onClick: () => setEditing(p) },
          { label: 'تعطيل', variant: 'danger', onClick: () => remove(p) },
        ]} />
      ),
    },
  ];

  return (
    <div>
      <h2 className={styles.pageTitle}>إدارة المنتجات</h2>
      <p className={styles.pageSub}>أضف منتجات جديدة، عدّل بياناتها وصورها، أو عطّلها.</p>

      <div className={styles.toolbar}>
        <label className={styles.search}>
          <SearchIcon size={16} />
          <input value={keyword} onChange={(e) => setKeyword(e.target.value)} placeholder="ابحث بالاسم أو الوصف..." />
        </label>
        <select value={categoryId} onChange={(e) => setCategoryId(e.target.value)}>
          <option value="">كل الفئات</option>
          {categories.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
        </select>
        <Button variant="primary" onClick={() => setEditing({})}>+ إضافة منتج</Button>
      </div>

      <DataTable columns={columns} rows={items} rowKey={(p) => p.id} loading={loading} error={error}
        onRetry={load} emptyTitle="لا منتجات مطابقة" emptyMessage="جرّب تعديل البحث أو الفئة، أو أضف منتجاً جديداً." />

      <Pagination page={page} totalPages={totalPages} onChange={setPage} />

      {editing !== null && (
        <ProductFormDrawer product={editing.id ? editing : null} categories={categories}
          onSave={save} onClose={() => setEditing(null)} />
      )}
    </div>
  );
}
