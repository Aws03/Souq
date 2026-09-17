import { useState, useEffect, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { api } from '../../api/client';
import { useAuth } from '../../context/AuthContext';
import DataTable from '../../components/common/DataTable';
import RowActionsMenu from '../../components/common/RowActionsMenu';
import Pagination from '../../components/common/Pagination';
import ProductImage from '../../components/product/ProductImage';
import { AdjustStockDrawer, StockMovementDrawer } from './StockDrawers';
import styles from './Admin.module.css';

const PAGE_SIZE = 50;

// مستوى المخزون بثلاث درجات لونية على المتاح (الموجود − المحجوز لطلبات لم تُدفع) — مطابق لـ isLowStock في الخادم:
// أحمر = بلغ حدّ التنبيه أو تحته، أصفر = ضِعف الحدّ أو أقل، أخضر = وفير.
function stockLevel(item) {
  if (item.available <= item.lowStockThreshold) return 'stockLow';
  if (item.available <= item.lowStockThreshold * 2) return 'stockWarn';
  return 'stockOk';
}

// شاشة جرد المخزون (المرحلة 6): صفّ لكل متغيّر (ADR-0039/0040) بوصفه وحالته — الموجود والمحجوز والمتاح (الأقلّ متاحاً
// أولاً) مرقّمة من الخادم، سجلّ الحركة، والتصحيح بفارق وسبب لمن يملك inventory.manage — لا تعيين مطلق يمحو بيعاً حدث أثناء فتح النموذج (Phase 0 C4).
// عدد المنخفض من الخادم (totalCount) لا من الصفحة الحالية — كي يبقى صحيحاً مع الترقيم.
export default function Inventory() {
  const { t } = useTranslation();
  const { can } = useAuth();
  const canManage = can('inventory.manage');
  const [items, setItems] = useState([]);
  const [page, setPage] = useState(1);
  const [totalPages, setTotalPages] = useState(1);
  const [lowCount, setLowCount] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [historyFor, setHistoryFor] = useState(null); // الصفّ (المتغيّر) المفتوح سجلّه، أو null
  const [adjusting, setAdjusting] = useState(null);   // الصفّ (المتغيّر) المفتوح تصحيحه، أو null

  // الاسم في العناوين: المنتج ووصف متغيّره إن كان له خيارات.
  const itemTitle = (item) => (item.variantLabel
    ? t('admin.inventory.itemName', { name: item.name, variant: item.variantLabel })
    : item.name);

  const load = useCallback(() => {
    setLoading(true);
    api.getInventory({ page, pageSize: PAGE_SIZE })
      .then((res) => { setItems(res.items); setTotalPages(res.totalPages); setError(null); })
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
    api.getLowStock({ pageSize: 1 }).then((res) => setLowCount(res.totalCount)).catch(() => setLowCount(0));
  }, [page]);

  useEffect(() => { load(); }, [load]);

  const columns = [
    { key: 'img', header: t('admin.inventory.colImage'), width: '64px', render: (p) => <div className={styles.thumb}><ProductImage product={p} /></div> },
    {
      key: 'name', header: t('admin.inventory.colName'), truncate: true, tooltip: (p) => p.name,
      render: (p) => (
        <div>
          <div>{p.name}</div>
          {p.variantLabel && <div className={styles.variantLabel} dir="auto">{p.variantLabel}</div>}
          {p.sku && <div className={styles.nameSecondary} dir="ltr">{p.sku}</div>}
          {p.variantIsActive === false && <span className={`${styles.statusBadge} ${styles.cancelled}`}>{t('admin.inventory.variantInactive')}</span>}
        </div>
      ),
    },
    { key: 'cat', header: t('admin.inventory.colCategory'), width: '130px', truncate: true, tooltip: (p) => p.categoryName, render: (p) => p.categoryName || '—' },
    { key: 'onHand', header: t('admin.inventory.colOnHand'), width: '80px', align: 'end', render: (p) => p.onHand },
    { key: 'reserved', header: t('admin.inventory.colReserved'), width: '80px', align: 'end', render: (p) => p.reserved },
    {
      key: 'available', header: t('admin.inventory.colAvailable'), width: '90px', align: 'end',
      render: (p) => <span className={`${styles.stockPill} ${styles[stockLevel(p)]}`}>{p.available}</span>,
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

      {!loading && !error && lowCount > 0 && (
        <div className={styles.lowStockAlert}>
          {t('admin.inventory.lowStockAlert', { count: lowCount })}
        </div>
      )}

      <DataTable columns={columns} rows={items} rowKey={(p) => p.variantId} loading={loading} error={error}
        onRetry={load} emptyTitle={t('admin.inventory.emptyTitle')} emptyMessage={t('admin.inventory.emptyMessage')}
        minWidth="760px" stickyFirstColumn />

      <Pagination page={page} totalPages={totalPages} onChange={setPage} />

      {historyFor && (
        <StockMovementDrawer variantId={historyFor.variantId} title={itemTitle(historyFor)} onClose={() => setHistoryFor(null)} />
      )}
      {adjusting && (
        <AdjustStockDrawer item={adjusting} title={itemTitle(adjusting)} onClose={() => setAdjusting(null)}
          onDone={() => { setAdjusting(null); load(); }} />
      )}
    </div>
  );
}

