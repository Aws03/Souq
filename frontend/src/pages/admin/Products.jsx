import { useState, useEffect, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { api } from '../../api/client';
import { useToast } from '../../context/ToastContext';
import DataTable from '../../components/common/DataTable';
import RowActionsMenu from '../../components/common/RowActionsMenu';
import Pagination from '../../components/common/Pagination';
import Button from '../../components/common/Button';
import { useConfirmAction } from '../../components/common/useConfirmAction';
import ProductImage from '../../components/product/ProductImage';
import { formatPrice, getCategoryName } from '../../components/product/ProductBadges';
import { SearchIcon } from '../../components/icons/Icons';
import { buildAdminProductQuery } from '../../features/admin/products/productQuery';
import ProductFormDrawer from './ProductFormDrawer';
import styles from './Admin.module.css';

const PAGE_SIZE = 10;
const STATUSES = ['Active', 'Draft', 'Archived'];
const STATUS_STYLE = { Active: 'delivered', Draft: 'pending', Archived: 'cancelled' };

// شاشة إدارة المنتجات (المرحلة 5): كل الحالات من /admin/products (مسودّة ونشط ومؤرشف — Phase 0 C7) ببحث وتصفية
// بالفئة والحالة، ودرج إضافة/تعديل يحمّل المنتج كاملاً (كل اللغات والصور بمعرّفاتها). لا حذف نهائي: الأرشفة تُخفي
// المنتج من المتجر وتُبقيه في الطلبات والتقارير، والاستعادة تعيده مسودّةً للمراجعة قبل النشر.
export default function Products() {
  const { t } = useTranslation();
  const toast = useToast();
  const confirmation = useConfirmAction();
  const [items, setItems] = useState([]);
  const [totalPages, setTotalPages] = useState(1);
  const [page, setPage] = useState(1);
  const [keyword, setKeyword] = useState('');
  const [categoryId, setCategoryId] = useState('');
  const [status, setStatus] = useState('');
  const [categories, setCategories] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [editing, setEditing] = useState(null); // null=مغلق، {}=إضافة، منتج كامل=تعديل

  useEffect(() => { api.getAdminCategories().then(setCategories).catch(() => {}); }, []);

  const load = useCallback(() => {
    setLoading(true);
    api.getAdminProducts(buildAdminProductQuery({ keyword, categoryId, status, page, pageSize: PAGE_SIZE }))
      .then((res) => { setItems(res.items); setTotalPages(res.totalPages); setError(null); })
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
  }, [keyword, categoryId, status, page]);

  useEffect(() => { load(); }, [load]);
  useEffect(() => { setPage(1); }, [keyword, categoryId, status]);

  // التعديل يحتاج المنتج كاملاً — سطر الجدول لا يحمل النصوص ولا الصور.
  const openEditor = async (product) => {
    try { setEditing(await api.getAdminProduct(product.id)); } catch (e) { toast.error(e.message); }
  };

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

  const applyStatus = async (product, target) => {
    await api.setProductStatus(product.id, target);
    toast.success(t(`admin.products.statusChanged.${target}`));
    load();
  };

  // الأرشفة وحدها تُؤكَّد: تُخفي المنتج من المتجر. النشر والإخفاء المؤقّت والاستعادة تُنفَّذ مباشرة كما كانت.
  const changeStatus = async (product, target) => {
    if (target === 'Archived') {
      confirmation.ask({
        title: t('admin.products.confirmArchive.title', { name: product.name }),
        message: t('admin.products.confirmArchive.message'),
        confirmLabel: t('admin.products.confirmArchive.action'),
        danger: true,
        action: () => applyStatus(product, target),
      });
      return;
    }
    try { await applyStatus(product, target); } catch (e) { toast.error(e.message); }
  };

  const statusActions = (p) => {
    const archive = { label: t('admin.products.archive'), variant: 'danger', onClick: () => changeStatus(p, 'Archived') };
    const publish = { label: t('admin.products.publish'), onClick: () => changeStatus(p, 'Active') };
    if (p.status === 'Active') return [{ label: t('admin.products.unpublish'), onClick: () => changeStatus(p, 'Draft') }, archive];
    if (p.status === 'Draft') return [publish, archive];
    return [{ label: t('admin.products.restore'), onClick: () => changeStatus(p, 'Draft') }, publish];
  };

  const columns = [
    { key: 'img', header: t('admin.products.colImage'), width: '70px', render: (p) => <div className={styles.thumb}><ProductImage product={p} /></div> },
    {
      key: 'name', header: t('admin.products.colName'), truncate: true, tooltip: (p) => p.name,
      render: (p) => (
        <div>
          <div>{p.name}</div>
          {p.sku && <div className={styles.nameSecondary} dir="ltr">{p.sku}</div>}
        </div>
      ),
    },
    { key: 'cat', header: t('admin.products.colCategory'), width: '140px', truncate: true, tooltip: (p) => p.categoryName, render: (p) => p.categoryName || '—' },
    {
      key: 'status', header: t('admin.products.colStatus'), width: '110px',
      render: (p) => <span className={`${styles.statusBadge} ${styles[STATUS_STYLE[p.status]] ?? ''}`}>{t(`admin.products.status.${p.status}`)}</span>,
    },
    {
      key: 'price', header: t('admin.products.colPrice'), width: '120px', align: 'end', render: (p) => (
        <div>
          <div>{formatPrice(p.price, p.currency)}</div>
          {p.compareAtPrice != null && <div className={styles.nameSecondary}><s>{formatPrice(p.compareAtPrice, p.currency)}</s></div>}
        </div>
      ),
    },
    { key: 'stock', header: t('admin.products.colStock'), width: '90px', align: 'end', render: (p) => p.available },
    {
      key: 'actions', header: t('admin.products.colActions'), width: '64px', align: 'end', render: (p) => (
        <RowActionsMenu actions={[{ label: t('common.edit'), onClick: () => openEditor(p) }, ...statusActions(p)]} />
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
        <select value={categoryId} onChange={(e) => setCategoryId(e.target.value)} aria-label={t('admin.products.colCategory')}>
          <option value="">{t('admin.products.allCategories')}</option>
          {categories.map((c) => <option key={c.id} value={c.id}>{getCategoryName(c)}</option>)}
        </select>
        <select value={status} onChange={(e) => setStatus(e.target.value)} aria-label={t('admin.products.colStatus')}>
          <option value="">{t('admin.products.allStatuses')}</option>
          {STATUSES.map((s) => <option key={s} value={s}>{t(`admin.products.status.${s}`)}</option>)}
        </select>
        <Button variant="primary" onClick={() => setEditing({})}>{t('admin.products.addProduct')}</Button>
      </div>

      <DataTable columns={columns} rows={items} rowKey={(p) => p.id} loading={loading} error={error}
        onRetry={load} emptyTitle={t('admin.products.emptyTitle')} emptyMessage={t('admin.products.emptyMessage')}
        minWidth="760px" stickyFirstColumn />

      <Pagination page={page} totalPages={totalPages} onChange={setPage} />

      {editing !== null && (
        <ProductFormDrawer product={editing.id ? editing : null} categories={categories}
          onSave={save} onImagesChanged={load} onClose={() => setEditing(null)} />
      )}
      {confirmation.dialog}
    </div>
  );
}
