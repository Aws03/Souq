import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../../api/client';
import { queryKeys } from '../../app/queryKeys';
import { useDebouncedValue } from '../../hooks/useDebouncedValue';
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
import StatusBadge from '../../components/common/StatusBadge';
import { statusTone } from '../../features/statusTone';

const PAGE_SIZE = 10;
const STATUSES = ['Active', 'Draft', 'Archived'];

// شاشة إدارة المنتجات (المرحلة 5): كل الحالات من /admin/products (مسودّة ونشط ومؤرشف — Phase 0 C7) ببحث وتصفية
// بالفئة والحالة، ودرج إضافة/تعديل يحمّل المنتج كاملاً (كل اللغات والصور بمعرّفاتها). لا حذف نهائي: الأرشفة تُخفي
// المنتج من المتجر وتُبقيه في الطلبات والتقارير، والاستعادة تعيده مسودّةً للمراجعة قبل النشر.
export default function Products() {
  const { t } = useTranslation();
  const toast = useToast();
  const confirmation = useConfirmAction();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [page, setPage] = useState(1);
  const [keyword, setKeyword] = useState('');
  const [categoryId, setCategoryId] = useState('');
  const [status, setStatus] = useState('');
  const [editing, setEditing] = useState(null); // null=مغلق، {}=إضافة، منتج كامل=تعديل

  const term = useDebouncedValue(keyword.trim());

  // فئات قائمة التصفية بالمفتاح نفسه الذي تستعمله شاشة الفئات، فزيارتهما تكلّف نداءً واحداً. وخطؤها
  // يُقرأ قائمةً فارغة كما كان (`.catch(() => {})` سابقاً): قائمةُ تصفيةٍ لا تُحمَّل لا تحجب الجدول.
  const { data: categories = [] } = useQuery({
    queryKey: queryKeys.adminCategories({}),
    queryFn: api.getAdminCategories,
  });

  // المفتاح يحمل معايير العرض كلّها (TD-25، M10): ثلاثة معايير هنا — بحثٌ وفئةٌ وحالة — وردٌّ لأيّ
  // تركيبةٍ تجاوزها المستخدم يُكتب في مفتاحه لا على الشاشة.
  const params = buildAdminProductQuery({ keyword: term, categoryId, status, page, pageSize: PAGE_SIZE });
  const { data, error, isPending, refetch } = useQuery({
    queryKey: queryKeys.adminProducts(params),
    queryFn: () => api.getAdminProducts(params),
    placeholderData: keepPreviousData,
  });

  const reload = () => queryClient.invalidateQueries({ queryKey: queryKeys.adminProductsAll() });
  // في المعالِج لا في تأثير: التصفية سببها ضغطة المستخدم.
  const filterBy = (setter) => (value) => { setter(value); setPage(1); };

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
    reload();
  };

  const applyStatus = async (product, target) => {
    await api.setProductStatus(product.id, target);
    toast.success(t(`admin.products.statusChanged.${target}`));
    reload();
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
          {p.variantCount > 1 && <div className={styles.nameSecondary}>{t('admin.products.variantsCount', { count: p.variantCount })}</div>}
        </div>
      ),
    },
    { key: 'cat', header: t('admin.products.colCategory'), width: '140px', truncate: true, tooltip: (p) => p.categoryName, render: (p) => p.categoryName || '—' },
    {
      key: 'status', header: t('admin.products.colStatus'), width: '110px',
      render: (p) => <StatusBadge tone={statusTone('product', p.status)}>{t(`admin.products.status.${p.status}`)}</StatusBadge>,
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
        <RowActionsMenu actions={[
          { label: t('common.edit'), onClick: () => openEditor(p) },
          { label: t('admin.products.manageVariants'), onClick: () => navigate(`/admin/products/${p.id}/variants`) },
          ...statusActions(p),
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
          <input value={keyword} onChange={(e) => filterBy(setKeyword)(e.target.value)} placeholder={t('admin.products.searchPlaceholder')} />
        </label>
        <select value={categoryId} onChange={(e) => filterBy(setCategoryId)(e.target.value)} aria-label={t('admin.products.colCategory')}>
          <option value="">{t('admin.products.allCategories')}</option>
          {categories.map((c) => <option key={c.id} value={c.id}>{getCategoryName(c)}</option>)}
        </select>
        <select value={status} onChange={(e) => filterBy(setStatus)(e.target.value)} aria-label={t('admin.products.colStatus')}>
          <option value="">{t('admin.products.allStatuses')}</option>
          {STATUSES.map((s) => <option key={s} value={s}>{t(`admin.products.status.${s}`)}</option>)}
        </select>
        <Button variant="primary" onClick={() => setEditing({})}>{t('admin.products.addProduct')}</Button>
      </div>

      <DataTable label={t('admin.products.title')} columns={columns} rows={data?.items ?? []} rowKey={(p) => p.id} loading={isPending}
        error={error?.message} onRetry={refetch} emptyTitle={t('admin.products.emptyTitle')} emptyMessage={t('admin.products.emptyMessage')}
        minWidth="760px" stickyFirstColumn />

      {data && <Pagination page={page} totalPages={data.totalPages} onChange={setPage} />}

      {editing !== null && (
        <ProductFormDrawer product={editing.id ? editing : null} categories={categories}
          onSave={save} onImagesChanged={reload} onClose={() => setEditing(null)}
          onManageVariants={(id) => navigate(`/admin/products/${id}/variants`)} />
      )}
      {confirmation.dialog}
    </div>
  );
}
