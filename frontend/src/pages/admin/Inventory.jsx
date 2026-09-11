import { useState, useEffect, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { api } from '../../api/client';
import DataTable from '../../components/common/DataTable';
import RowActionsMenu from '../../components/common/RowActionsMenu';
import Drawer from '../../components/common/Drawer';
import Pagination from '../../components/common/Pagination';
import ProductImage from '../../components/product/ProductImage';
import { EmptyState, ErrorBanner } from '../../components/common/StateViews';
import Skeleton from '../../components/common/Skeleton';
import { getProductName } from '../../components/product/ProductBadges';
import { formatDate } from '../../i18n';
import styles from './Admin.module.css';

const PAGE_SIZE = 50;
const MOVEMENTS_PAGE_SIZE = 20;

// مستوى المخزون بثلاث درجات لونية (منطق واحد للحقيقة، لا ألوان سحرية متناثرة):
// أحمر = بلغ حدّ التنبيه أو تحته (منخفض فعلاً، مطابق لـ isLowStock في الخادم)،
// أصفر = ضِعف الحدّ أو أقل (يقترب)، أخضر = وفير.
function stockLevel(item) {
  if (item.stockQuantity <= item.lowStockThreshold) return 'stockLow';
  if (item.stockQuantity <= item.lowStockThreshold * 2) return 'stockWarn';
  return 'stockOk';
}

// شاشة جرد المخزون: المنتجات النشطة بمخزونها الحالي (الأقلّ أولاً) مرقّمة من الخادم،
// ملوّنة حسب المستوى، مع درج يعرض سجلّ حركة المخزون لكل منتج (بيع/توريد/تصحيح).
// عدد المنخفض يأتي من الخادم (totalCount) لا من الصفحة الحالية — كي يبقى صحيحاً مع الترقيم.
export default function Inventory() {
  const { t } = useTranslation();
  const [items, setItems] = useState([]);
  const [page, setPage] = useState(1);
  const [totalPages, setTotalPages] = useState(1);
  const [lowCount, setLowCount] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [historyFor, setHistoryFor] = useState(null); // المنتج المفتوح سجلّه، أو null

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
          {p.sku && <div className={styles.nameSecondary} dir="ltr">{p.sku}</div>}
        </div>
      ),
    },
    { key: 'cat', header: t('admin.inventory.colCategory'), width: '140px', truncate: true, tooltip: (p) => p.categoryName, render: (p) => p.categoryName || '—' },
    {
      key: 'stock', header: t('admin.inventory.colStock'), width: '110px', align: 'end',
      render: (p) => <span className={`${styles.stockPill} ${styles[stockLevel(p)]}`}>{p.stockQuantity}</span>,
    },
    { key: 'threshold', header: t('admin.inventory.colThreshold'), width: '90px', align: 'end', render: (p) => p.lowStockThreshold },
    {
      key: 'actions', header: t('admin.inventory.colActions'), width: '64px', align: 'end', render: (p) => (
        <RowActionsMenu actions={[
          { label: t('admin.inventory.viewHistory'), onClick: () => setHistoryFor(p) },
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

      <DataTable columns={columns} rows={items} rowKey={(p) => p.id} loading={loading} error={error}
        onRetry={load} emptyTitle={t('admin.inventory.emptyTitle')} emptyMessage={t('admin.inventory.emptyMessage')}
        minWidth="620px" stickyFirstColumn />

      <Pagination page={page} totalPages={totalPages} onChange={setPage} />

      {historyFor && (
        <StockMovementDrawer product={historyFor} onClose={() => setHistoryFor(null)} />
      )}
    </div>
  );
}

// درج سجلّ حركة المخزون لمنتج واحد — مرقّم من الخادم (السجلّ ينمو مع كل بيع)، الأحدث أولاً.
function StockMovementDrawer({ product, onClose }) {
  const { t } = useTranslation();
  const [movements, setMovements] = useState(null);
  const [page, setPage] = useState(1);
  const [totalPages, setTotalPages] = useState(1);
  const [error, setError] = useState(null);

  useEffect(() => {
    let alive = true;
    api.getStockMovements(product.id, { page, pageSize: MOVEMENTS_PAGE_SIZE })
      .then((res) => { if (alive) { setMovements(res.items); setTotalPages(res.totalPages); } })
      .catch((e) => { if (alive) setError(e.message); });
    return () => { alive = false; };
  }, [product.id, page]);

  return (
    <Drawer open onClose={onClose} side="right" title={t('admin.inventory.historyTitle', { name: getProductName(product) })}>
      {error && <ErrorBanner message={error} />}
      {!error && movements === null && (
        <div className={styles.movementList}>
          {Array.from({ length: 4 }).map((_, i) => <Skeleton key={i} height={54} />)}
        </div>
      )}
      {!error && movements !== null && movements.length === 0 && (
        <EmptyState title={t('admin.inventory.noMovements')} />
      )}
      {!error && movements !== null && movements.length > 0 && (
        <>
          <ul className={styles.movementList}>
            {movements.map((m) => (
              <li key={m.id} className={styles.movementRow}>
                <span className={`${styles.moveType} ${styles[`move_${m.type.toLowerCase()}`]}`}>
                  {t(`admin.inventory.moveType.${m.type}`, { defaultValue: m.type })}
                </span>
                <span className={`${styles.moveDelta} ${m.quantityChange < 0 ? styles.moveDown : styles.moveUp}`}>
                  {m.quantityChange > 0 ? `+${m.quantityChange}` : m.quantityChange}
                </span>
                <span className={styles.moveMeta}>
                  {t('admin.inventory.newQuantity', { qty: m.newQuantity })}
                  {m.note ? ` · ${m.note}` : ''}
                </span>
                <span className={styles.moveDate}>{formatDate(m.createdAt)}</span>
              </li>
            ))}
          </ul>
          <Pagination page={page} totalPages={totalPages} onChange={setPage} />
        </>
      )}
    </Drawer>
  );
}
