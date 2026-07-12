import { useTranslation } from 'react-i18next';
import Skeleton from './Skeleton';
import { EmptyState, ErrorBanner } from './StateViews';
import styles from './DataTable.module.css';

/**
 * جدول بيانات عام لكل شاشات لوحة الإدارة: صفوف بألوان متبادلة، هيكل تحميل،
 * حالة فارغة مصمَّمة، ولافتة خطأ مع إعادة محاولة — بدل تكرار كل ذلك في كل شاشة.
 *
 * كل عمود يقبل: key, header, render(row)، وأيضاً اختيارياً width (يُطبَّق عبر
 * <col> — الطريقة الدلالية الصحيحة لعرض عمود جدول ثابت، بلا حاجة لـ CSS خارجي
 * لكل تركيبة أعمدة)، align ('start' افتراضي نصّي، أو 'end' للأرقام — منطقي
 * يعكس نفسه تلقائياً بين RTL/LTR)، truncate (قصّ بنقاط + title عند التمرير —
 * للأعمدة النصّية التي قد تطول، لا لأعمدة تحوي صوراً/شارات/أزرار)، وtooltip(row)
 * لنص title المصاحب. stickyFirstColumn يثبّت أول عمود عند التمرير الأفقي بالجوال.
 */
export default function DataTable({
  columns, rows, rowKey, loading, error, onRetry,
  emptyTitle, emptyMessage, skeletonRows = 5, minWidth, stickyFirstColumn = false,
}) {
  const { t } = useTranslation();
  if (error) return <ErrorBanner message={error} onRetry={onRetry} />;

  const cellClass = (c, i) =>
    [
      styles[c.align === 'end' ? 'alignEnd' : 'alignStart'],
      c.truncate ? styles.truncate : '',
      stickyFirstColumn && i === 0 ? styles.stickyCol : '',
    ].filter(Boolean).join(' ');

  return (
    <div className={styles.wrap}>
      {/* minWidth قيمة ديناميكية لكل استدعاء (تختلف بعدد أعمدة كل جدول) — لا يوجد
          صنف CSS ثابت ممكن لها، فتبقى style هنا بدل تكرار Module لكل تركيبة. */}
      <table className={styles.table} style={minWidth ? { minWidth } : undefined}>
        <colgroup>
          {columns.map((c) => <col key={c.key} style={c.width ? { width: c.width } : undefined} />)}
        </colgroup>
        <thead>
          <tr>
            {columns.map((c, i) => (
              <th key={c.key} className={cellClass(c, i)}>{c.header}</th>
            ))}
          </tr>
        </thead>
        <tbody>
          {loading && Array.from({ length: skeletonRows }).map((_, i) => (
            <tr key={`sk-${i}`}>
              {columns.map((c, ci) => <td key={c.key} className={cellClass(c, ci)}><Skeleton height={18} /></td>)}
            </tr>
          ))}
          {!loading && rows.map((row) => (
            <tr key={rowKey(row)}>
              {columns.map((c, i) => (
                <td key={c.key} className={cellClass(c, i)} title={c.tooltip?.(row)}>
                  {c.render(row)}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
      {!loading && rows.length === 0 && (
        <EmptyState title={emptyTitle ?? t('common.noDataYet')} message={emptyMessage} />
      )}
    </div>
  );
}
