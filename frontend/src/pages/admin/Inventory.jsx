import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../../api/client';
import { queryKeys } from '../../app/queryKeys';
import { useAuth } from '../../context/AuthContext';
import DataTable from '../../components/common/DataTable';
import RowActionsMenu from '../../components/common/RowActionsMenu';
import Pagination from '../../components/common/Pagination';
import ProductImage from '../../components/product/ProductImage';
import { AdjustStockDrawer, StockMovementDrawer } from './StockDrawers';
import styles from './Admin.module.css';
import StatusBadge from '../../components/common/StatusBadge';
import { flagTone, stockTone } from '../../features/statusTone';

const PAGE_SIZE = 50;

// شاشة جرد المخزون (المرحلة 6): صفّ لكل متغيّر (ADR-0039/0040) بوصفه وحالته — الموجود والمحجوز والمتاح (الأقلّ متاحاً
// أولاً) مرقّمة من الخادم، سجلّ الحركة، والتصحيح بفارق وسبب لمن يملك inventory.manage — لا تعيين مطلق يمحو بيعاً حدث أثناء فتح النموذج (Phase 0 C4).
// عدد المنخفض من الخادم (totalCount) لا من الصفحة الحالية — كي يبقى صحيحاً مع الترقيم.
//
// **على طبقة الاستعلام (ADR-0037/0038، جزء من TD-25 أُغلق في M7).** كانت الشاشة تحمّل بـ `useEffect` + `load()`،
// فنداءان معلّقان على صفحتين يصلان بأي ترتيب: ردٌّ لصفحة تجاوزها المتجر يكتب فوق المعروض، فتُقرأ أرقام مخزون
// صفحةٍ أخرى — وهذه شاشة يقرأ منها التاجر قراراً. المفتاح يحمل الصفحة، فالردّ يُكتب في مفتاحه لا على الشاشة،
// و`keepPreviousData` يُبقي الصفحة السابقة مقروءة أثناء جلب التالية بدل جدول فارغ. اختبار Inventory.test.jsx
// يثبّت هذا صراحةً. أما درج السجلّ (StockDrawers) فكان يحرس نفسه بعلم `alive` أصلاً، فلم يُمسّ.
export default function Inventory() {
  const { t } = useTranslation();
  const { can } = useAuth();
  const queryClient = useQueryClient();
  const canManage = can('inventory.manage');
  const [page, setPage] = useState(1);
  const [historyFor, setHistoryFor] = useState(null); // الصفّ (المتغيّر) المفتوح سجلّه، أو null
  const [adjusting, setAdjusting] = useState(null);   // الصفّ (المتغيّر) المفتوح تصحيحه، أو null

  // الاسم في العناوين: المنتج ووصف متغيّره إن كان له خيارات.
  const itemTitle = (item) => (item.variantLabel
    ? t('admin.inventory.itemName', { name: item.name, variant: item.variantLabel })
    : item.name);

  const { data, error, isPending, refetch } = useQuery({
    queryKey: queryKeys.inventory(page, PAGE_SIZE),
    queryFn: () => api.getInventory({ page, pageSize: PAGE_SIZE }),
    placeholderData: keepPreviousData,
  });

  // عدد المنخفض لا يتعلّق بالصفحة المعروضة، فله مفتاحه: التصفّح لا يعيد سؤاله. وخطؤه يُقرأ صفراً (كما كان)
  // لأنه شريط تنبيه لا محتوى الشاشة — فشله لا يجوز أن يحجب الجرد نفسه.
  const { data: low } = useQuery({
    queryKey: queryKeys.inventoryLowCount(),
    queryFn: () => api.getLowStock({ pageSize: 1 }),
  });
  const lowCount = low?.totalCount ?? 0;

  const items = data?.items ?? [];
  const reload = () => queryClient.invalidateQueries({ queryKey: queryKeys.inventoryAll() });

  const columns = [
    { key: 'img', header: t('admin.inventory.colImage'), width: '64px', render: (p) => <div className={styles.thumb}><ProductImage product={p} /></div> },
    {
      key: 'name', header: t('admin.inventory.colName'), truncate: true, tooltip: (p) => p.name,
      render: (p) => (
        <div>
          <div>{p.name}</div>
          {p.variantLabel && <div className={styles.variantLabel} dir="auto">{p.variantLabel}</div>}
          {p.sku && <div className={styles.nameSecondary} dir="ltr">{p.sku}</div>}
          {p.variantIsActive === false && <StatusBadge tone={flagTone(false)}>{t('admin.inventory.variantInactive')}</StatusBadge>}
        </div>
      ),
    },
    { key: 'cat', header: t('admin.inventory.colCategory'), width: '130px', truncate: true, tooltip: (p) => p.categoryName, render: (p) => p.categoryName || '—' },
    { key: 'onHand', header: t('admin.inventory.colOnHand'), width: '80px', align: 'end', render: (p) => p.onHand },
    { key: 'reserved', header: t('admin.inventory.colReserved'), width: '80px', align: 'end', render: (p) => p.reserved },
    {
      key: 'available', header: t('admin.inventory.colAvailable'), width: '90px', align: 'end',
      render: (p) => <StatusBadge tone={stockTone(p)} shape="pill">{p.available}</StatusBadge>,
    },
    { key: 'threshold', header: t('admin.inventory.colThreshold'), width: '80px', align: 'end', render: (p) => p.lowStockThreshold },
    {
      key: 'actions', header: t('admin.inventory.colActions'), width: '64px', align: 'end', render: (p) => (
        <RowActionsMenu actions={[
          { label: t('admin.inventory.viewHistory'), onClick: () => setHistoryFor(p) },
          ...(canManage ? [{ label: t('admin.inventory.adjust'), onClick: () => setAdjusting(p) }] : []),
        ]} />
      ),
    },
  ];

  return (
    <div>
      <h2 className={styles.pageTitle}>{t('admin.inventory.title')}</h2>
      <p className={styles.pageSub}>{t('admin.inventory.subtitle')}</p>

      {!isPending && !error && lowCount > 0 && (
        <div className={styles.lowStockAlert}>
          {t('admin.inventory.lowStockAlert', { count: lowCount })}
        </div>
      )}

      <DataTable label={t('admin.inventory.title')} columns={columns} rows={items} rowKey={(p) => p.variantId} loading={isPending}
        error={error?.message} onRetry={refetch} emptyTitle={t('admin.inventory.emptyTitle')}
        emptyMessage={t('admin.inventory.emptyMessage')} minWidth="760px" stickyFirstColumn />

      {data && <Pagination page={page} totalPages={data.totalPages} onChange={setPage} />}

      {historyFor && (
        <StockMovementDrawer variantId={historyFor.variantId} title={itemTitle(historyFor)} onClose={() => setHistoryFor(null)} />
      )}
      {adjusting && (
        <AdjustStockDrawer item={adjusting} title={itemTitle(adjusting)} onClose={() => setAdjusting(null)}
          onDone={() => { setAdjusting(null); reload(); }} />
      )}
    </div>
  );
}

