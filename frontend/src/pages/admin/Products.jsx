import { useState, useEffect, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { api } from '../../api/client';
import { useToast } from '../../context/ToastContext';
import DataTable from '../../components/common/DataTable';
import RowActionsMenu from '../../components/common/RowActionsMenu';
import Pagination from '../../components/common/Pagination';
import Button from '../../components/common/Button';
import ProductImage from '../../components/product/ProductImage';
import { formatPrice, getProductName } from '../../components/product/ProductBadges';
import { SearchIcon } from '../../components/icons/Icons';
import ProductFormDrawer from './ProductFormDrawer';
import styles from './Admin.module.css';

const PAGE_SIZE = 10;

// شاشة إدارة المنتجات: جدول ببحث/تصفية/ترقيم حقيقية من الـ API + درج
// إضافة/تعديل بسحب وإفلات صورة. الحذف حذف منطقي (تعطيل) — يختفي المنتج من
// القائمة فوراً لأن GET /products يعرض النشط فقط.
export default function Products() {
  const { t } = useTranslation();
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

  const save = async (payload, file, videoFile) => {
    const id = editing?.id
      ? (await api.updateProduct(editing.id, payload), editing.id)
      : (await api.createProduct(payload)).id;
    if (file) await api.uploadProductImage(id, file);
    if (videoFile) await api.uploadProductVideo(id, videoFile);
    setEditing(null);
    toast.success(editing?.id ? t('admin.products.updated') : t('admin.products.created'));
    load();
  };

  const remove = async (product) => {
    if (!window.confirm(t('admin.products.confirmDisable', { name: getProductName(product) }))) return;
    try {
      await api.deleteProduct(product.id);
      toast.success(t('admin.products.disabled'));
      load();
    } catch (e) { toast.error(e.message); }
  };

  const columns = [
    { key: 'img', header: t('admin.products.colImage'), width: '70px', render: (p) => <div className={styles.thumb}><ProductImage product={p} /></div> },
    {
      key: 'name', header: t('admin.products.colName'), truncate: true, tooltip: (p) => p.nameEn ? `${p.nameAr} / ${p.nameEn}` : p.nameAr,
      render: (p) => (
        <div>
          <div>{p.nameAr}</div>
          {p.nameEn && p.nameEn !== p.nameAr && <div className={styles.nameSecondary}>{p.nameEn}</div>}
        </div>
      ),
    },
    { key: 'cat', header: t('admin.products.colCategory'), width: '140px', truncate: true, tooltip: (p) => p.categoryName, render: (p) => p.categoryName || '—' },
    { key: 'price', header: t('admin.products.colPrice'), width: '110px', align: 'end', render: (p) => formatPrice(p.price, p.currency) },
    { key: 'stock', header: t('admin.products.colStock'), width: '90px', align: 'end', render: (p) => p.stockQuantity },
    {
      key: 'actions', header: t('admin.products.colActions'), width: '64px', align: 'end', render: (p) => (
        <RowActionsMenu actions={[
          { label: t('common.edit'), onClick: () => setEditing(p) },
          { label: t('common.disable'), variant: 'danger', onClick: () => remove(p) },
        ]} />
      ),
    },
  ];

  return (
    <div>
      <h2 className={styles.pageTitle}>{t('admin.products.title')}</h2>
      <p className={styles.pageSub}>{t('admin.products.subtitle')}</p>

      <div className={styles.toolbar}>
        <label className={styles.search}>
          <SearchIcon size={16} />
          <input value={keyword} onChange={(e) => setKeyword(e.target.value)} placeholder={t('admin.products.searchPlaceholder')} />
        </label>
        <select value={categoryId} onChange={(e) => setCategoryId(e.target.value)}>
          <option value="">{t('admin.products.allCategories')}</option>
          {categories.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
        </select>
        <Button variant="primary" onClick={() => setEditing({})}>{t('admin.products.addProduct')}</Button>
      </div>

      <DataTable columns={columns} rows={items} rowKey={(p) => p.id} loading={loading} error={error}
        onRetry={load} emptyTitle={t('admin.products.emptyTitle')} emptyMessage={t('admin.products.emptyMessage')}
        minWidth="620px" stickyFirstColumn />

      <Pagination page={page} totalPages={totalPages} onChange={setPage} />

      {editing !== null && (
        <ProductFormDrawer product={editing.id ? editing : null} categories={categories}
          onSave={save} onClose={() => setEditing(null)} />
      )}
    </div>
  );
}
