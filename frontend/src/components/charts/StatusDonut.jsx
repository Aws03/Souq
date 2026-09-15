import { donutSlices } from '../../features/reporting/chartScales';
import styles from './Charts.module.css';

const SIZE = 160;
const STROKE = 22;
const RADIUS = (SIZE - STROKE) / 2;
const CIRCUMFERENCE = 2 * Math.PI * RADIUS;

// ============================================================================
// حلقة حصص — تُستعمل هنا لحالات الطلبات وحدها، وهي الحالة التي تفيد فيها فعلاً: خمس فئات
// يُسأل عنها كنِسَب من كلّ ("كم نسبة ما زال بانتظار الدفع؟").
//
// مرسومة بـstroke-dasharray على دوائر متراكبة لا بأقواس محسوبة: لا رياضيات زوايا تُخطئ عند
// 100%، ولا مسار يختفي حين تكون الحصّة كاملة.
// ============================================================================
export default function StatusDonut({ items, totalLabel, total }) {
  const { slices } = donutSlices(items);

  return (
    <div className={styles.donutWrap}>
      <svg viewBox={`0 0 ${SIZE} ${SIZE}`} className={styles.donutSvg}>
        <g transform={`rotate(-90 ${SIZE / 2} ${SIZE / 2})`}>
          <circle cx={SIZE / 2} cy={SIZE / 2} r={RADIUS} className={styles.donutTrack} strokeWidth={STROKE} fill="none" />
          {slices.map((slice) => (
            <circle
              key={slice.label}
              cx={SIZE / 2} cy={SIZE / 2} r={RADIUS}
              fill="none"
              strokeWidth={STROKE}
              stroke={slice.color}
              strokeDasharray={`${slice.fraction * CIRCUMFERENCE} ${CIRCUMFERENCE}`}
              strokeDashoffset={-slice.offset * CIRCUMFERENCE}
            />
          ))}
        </g>
      </svg>

      <div className={styles.donutCentre} aria-hidden="true">
        <b>{total}</b>
        <span>{totalLabel}</span>
      </div>

      <ul className={styles.legend}>
        {slices.map((slice) => (
          <li key={slice.label}>
            <span className={styles.legendDot} style={{ background: slice.color }} />
            <span className={styles.legendLabel}>{slice.label}</span>
            <span className={styles.legendValue}>{slice.value}</span>
          </li>
        ))}
      </ul>
    </div>
  );
}
