import { useState, useEffect, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { api } from '../../api/client';
import { useAuth } from '../../context/AuthContext';
import { useToast } from '../../context/ToastContext';
import DataTable from '../../components/common/DataTable';
import RowActionsMenu from '../../components/common/RowActionsMenu';
import Drawer from '../../components/common/Drawer';
import Pagination from '../../components/common/Pagination';
import Button from '../../components/common/Button';
import FormField, { inputClass } from '../../components/common/FormField';
import ProductImage from '../../components/product/ProductImage';
import { EmptyState, ErrorBanner } from '../../components/common/StateViews';
import Skeleton from '../../components/common/Skeleton';
import { getProductName } from '../../components/product/ProductBadges';
import { formatDate } from '../../i18n';
import styles from './Admin.module.css';
import formStyles from './CategoryFormDrawer.module.css';

const PAGE_SIZE = 50;
const MOVEMENTS_PAGE_SIZE = 20;

// مستوى المخزون بثلاث درجات لونية على المتاح (الموجود − المحجوز لطلبات لم تُدفع) — مطابق لـ isLowStock في الخادم:
// أحمر = بلغ حدّ التنبيه أو تحته، أصفر = ضِعف الحدّ أو أقل، أخضر = وفير.
function stockLevel(item) {
  if (item.available <= item.lowStockThreshold) return 'stockLow';
  if (item.available <= item.lowStockThreshold * 2) return 'stockWarn';
  return 'stockOk';
}

// شاشة جرد المخزون (المرحلة 6): لكل منتج الموجود والمحجوز والمتاح (الأقلّ متاحاً أولاً) مرقّمة من الخادم، سجلّ الحركة،
// والتصحيح بفارق وسبب لمن يملك inventory.manage — لا تعيين مطلق يمحو بيعاً حدث أثناء فتح النموذج (Phase 0 C4).
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
  const [historyFor, setHistoryFor] = useState(null); // المنتج المفتوح سجلّه، أو null
  const [adjusting, setAdjusting] = useState(null);   // المنتج المفتوح تصحيحه، أو null

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

      <DataTable columns={columns} rows={items} rowKey={(p) => p.id} loading={loading} error={error}
        onRetry={load} emptyTitle={t('admin.inventory.emptyTitle')} emptyMessage={t('admin.inventory.emptyMessage')}
        minWidth="760px" stickyFirstColumn />

      <Pagination page={page} totalPages={totalPages} onChange={setPage} />

      {historyFor && (
        <StockMovementDrawer product={historyFor} onClose={() => setHistoryFor(null)} />
      )}
      {adjusting && (
        <AdjustStockDrawer item={adjusting} onClose={() => setAdjusting(null)}
          onDone={() => { setAdjusting(null); load(); }} />
      )}
    </div>
  );
}

// درج التصحيح: فارق (زيادة أو نقص) مع سببه يُضاف للكمية الحالية على الخادم، وحدّ التنبيه. الخادم يرفض النزول تحت
// المحجوز لطلبات قائمة برسالة واضحة.
function AdjustStockDrawer({ item, onClose, onDone }) {
  const { t } = useTranslation();
  const toast = useToast();
  const [delta, setDelta] = useState('');
  const [reason, setReason] = useState('');
  const [threshold, setThreshold] = useState(item.lowStockThreshold);
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);

  const submit = async (e) => {
    e.preventDefault();
    const change = Number(delta) || 0;
    const thresholdChanged = threshold !== '' && Number(threshold) !== item.lowStockThreshold;
    if (!change && !thresholdChanged) return setError(t('admin.inventory.nothingToSave'));
    if (change && !reason.trim()) return setError(t('admin.inventory.reasonRequired'));

    setBusy(true); setError(null);
    try {
      if (change) await api.adjustStock(item.id, change, reason.trim());
      if (thresholdChanged) await api.setStockThreshold(item.id, Number(threshold));
      toast.success(t('admin.inventory.adjusted'));
      onDone();
    } catch (err) { setError(err.message); setBusy(false); }
  };

  return (
    <Drawer open onClose={onClose} side="right" busy={busy} title={t('admin.inventory.adjustTitle', { name: getProductName(item) })}
      footer={
        <div className={formStyles.footActions}>
          <Button variant="ghost" onClick={onClose} disabled={busy}>{t('common.cancel')}</Button>
          <Button variant="primary" type="submit" form="adjust-stock-form" loading={busy}>{t('common.save')}</Button>
        </div>
      }>
      <form id="adjust-stock-form" onSubmit={submit}>
        {error && <ErrorBanner message={error} />}
        <p className={styles.nameSecondary}>
          {t('admin.inventory.currentLevel', { onHand: item.onHand, reserved: item.reserved, available: item.available })}
        </p>
        <FormField label={t('admin.inventory.deltaLabel')} hint={t('admin.inventory.deltaHint')}>
          <input className={inputClass(false)} type="number" step="1" value={delta} dir="ltr" placeholder="+10 / -2"
            onChange={(e) => setDelta(e.target.value)} />
        </FormField>
        <FormField label={t('admin.inventory.reasonLabel')}>
          <input className={inputClass(false)} value={reason} maxLength={200} placeholder={t('admin.inventory.reasonPlaceholder')}
            onChange={(e) => setReason(e.target.value)} />
        </FormField>
        <FormField label={t('admin.inventory.thresholdLabel')}>
          <input className={inputClass(false)} type="number" min="0" step="1" value={threshold}
            onChange={(e) => setThreshold(e.target.value)} />
        </FormField>
      </form>
    </Drawer>
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
