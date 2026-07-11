import Skeleton from './Skeleton';
import { EmptyState, ErrorBanner } from './StateViews';
import styles from './DataTable.module.css';

/**
 * جدول بيانات عام لكل شاشات لوحة الإدارة: صفوف بألوان متبادلة، هيكل تحميل،
 * حالة فارغة مصمَّمة، ولافتة خطأ مع إعادة محاولة — بدل تكرار كل ذلك في كل شاشة.
 */
export default function DataTable({
  columns, rows, rowKey, loading, error, onRetry,
  emptyTitle = 'لا بيانات بعد', emptyMessage, skeletonRows = 5,
}) {
  if (error) return <ErrorBanner message={error} onRetry={onRetry} />;

  return (
    <div className={styles.wrap}>
      <table className={styles.table}>
        <thead>
          <tr>{columns.map((c) => <th key={c.key}>{c.header}</th>)}</tr>
        </thead>
        <tbody>
          {loading && Array.from({ length: skeletonRows }).map((_, i) => (
            <tr key={`sk-${i}`}>
              {columns.map((c) => <td key={c.key}><Skeleton height={18} /></td>)}
            </tr>
          ))}
          {!loading && rows.map((row) => (
            <tr key={rowKey(row)}>
              {columns.map((c) => <td key={c.key}>{c.render(row)}</td>)}
            </tr>
          ))}
        </tbody>
      </table>
      {!loading && rows.length === 0 && (
        <EmptyState title={emptyTitle} message={emptyMessage} />
      )}
    </div>
  );
}
