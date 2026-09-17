import { useState, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { api } from '../../api/client';
import { useToast } from '../../context/ToastContext';
import Drawer from '../../components/common/Drawer';
import Pagination from '../../components/common/Pagination';
import Button from '../../components/common/Button';
import FormField, { inputClass } from '../../components/common/FormField';
import { EmptyState, ErrorBanner } from '../../components/common/StateViews';
import Skeleton from '../../components/common/Skeleton';
import { formatDate } from '../../i18n';
import styles from './Admin.module.css';
import formStyles from './CategoryFormDrawer.module.css';

const MOVEMENTS_PAGE_SIZE = 20;

// ============================================================================
// درجا المخزون المشتركان بين شاشة الجرد وصفحة متغيّرات المنتج — بمسارات المتغيّر (ADR-0039): الصفّ متغيّر، ولمنتج بسيط هو
// متغيّره الوحيد. item: { variantId, onHand, reserved, available, lowStockThreshold }، والعنوان يبنيه المستدعي (اسم ووصف).
// ============================================================================

// درج التصحيح: فارق (زيادة أو نقص) مع سببه يُضاف للكمية الحالية على الخادم، وحدّ التنبيه. الخادم يرفض النزول تحت
// المحجوز لطلبات قائمة برسالة واضحة.
export function AdjustStockDrawer({ item, title, onClose, onDone }) {
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
      if (change) await api.adjustVariantStock(item.variantId, change, reason.trim());
      if (thresholdChanged) await api.setVariantStockThreshold(item.variantId, Number(threshold));
      toast.success(t('admin.inventory.adjusted'));
      onDone();
    } catch (err) { setError(err.message); setBusy(false); }
  };

  return (
    <Drawer open onClose={onClose} side="right" busy={busy} title={t('admin.inventory.adjustTitle', { name: title })}
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

// درج سجلّ حركة مخزون متغيّر واحد — مرقّم من الخادم (السجلّ ينمو مع كل بيع)، الأحدث أولاً.
export function StockMovementDrawer({ variantId, title, onClose }) {
  const { t } = useTranslation();
  const [movements, setMovements] = useState(null);
  const [page, setPage] = useState(1);
  const [totalPages, setTotalPages] = useState(1);
  const [error, setError] = useState(null);

  useEffect(() => {
    let alive = true;
    api.getVariantStockMovements(variantId, { page, pageSize: MOVEMENTS_PAGE_SIZE })
      .then((res) => { if (alive) { setMovements(res.items); setTotalPages(res.totalPages); } })
      .catch((e) => { if (alive) setError(e.message); });
    return () => { alive = false; };
  }, [variantId, page]);

  return (
    <Drawer open onClose={onClose} side="right" title={t('admin.inventory.historyTitle', { name: title })}>
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
