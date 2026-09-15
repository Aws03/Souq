import { barWidths } from '../../features/reporting/chartScales';
import styles from './Charts.module.css';

// ============================================================================
// قائمة أشرطة أفقية — الشكل الصحيح لترتيب مُسمّى (أكثر المنتجات مبيعاً، أداء الفئات).
// أعمدة رأسية بأسماء منتجات تعني نصّاً مائلاً أو مقصوصاً؛ الشريط الأفقي يترك للاسم سطره.
// الشريط نسبةٌ إلى الأكبر لا إلى المجموع: المقارنة هنا بين صفوف لا حصص من كلّ.
// ============================================================================
export default function BarList({ rows, formatValue }) {
  const widths = barWidths(rows.map((r) => r.value));

  return (
    <ol className={styles.barList}>
      {rows.map((row, i) => (
        <li key={row.id ?? row.label} className={styles.barRow}>
          <div className={styles.barHead}>
            <span className={styles.barLabel}>{row.label}</span>
            <span className={styles.barValue}>{formatValue(row)}</span>
          </div>
          {/* عرض الشريط نسبة مئوية لا بكسلات: يتبع عرض الحاوية في أي مقاس. */}
          <div className={styles.barTrack}>
            <div className={styles.barFill} style={{ inlineSize: `${Math.max(widths[i] * 100, row.value > 0 ? 2 : 0)}%` }} />
          </div>
        </li>
      ))}
    </ol>
  );
}
