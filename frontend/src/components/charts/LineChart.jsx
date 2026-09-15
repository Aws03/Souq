import { useId, useState } from 'react';
import { areaPath, linePath, linePoints, verticalScale } from '../../features/reporting/chartScales';
import styles from './Charts.module.css';

const WIDTH = 640;
const HEIGHT = 200;
const PAD = 8;

// ============================================================================
// منحنى زمني. الاتجاه (RTL) يُعالَج بانعكاس عنصر الرسم وحده لا بحساب مقلوب: الحساب يبقى
// واحداً، والنصّ خارج الـSVG فلا ينعكس. المحور يبدأ من صفر دائماً (chartScales).
//
// التفاعل باللمس والفأرة معاً: شريط رأسي يتبع أقرب نقطة، لا "tooltip" يحتاج تحويماً دقيقاً
// (وهو ما يجعل المخطّطات عديمة الفائدة على الجوال).
// ============================================================================
export default function LineChart({ points, formatValue, formatLabel, rtl = false }) {
  const [active, setActive] = useState(null);
  const gradientId = useId();

  const values = points.map((p) => p.value);
  const scale = verticalScale(values);
  const coords = linePoints(values, { width: WIDTH, height: HEIGHT, max: scale.max });

  const onMove = (event) => {
    const box = event.currentTarget.getBoundingClientRect();
    const ratio = (event.clientX - box.left) / box.width;
    const position = rtl ? 1 - ratio : ratio;
    const index = Math.round(position * (points.length - 1));
    setActive(Math.min(points.length - 1, Math.max(0, index)));
  };

  const current = active === null ? null : points[active];

  return (
    <div className={styles.lineWrap}>
      <svg
        viewBox={`0 0 ${WIDTH} ${HEIGHT + PAD * 2}`}
        className={styles.lineSvg}
        style={rtl ? { transform: 'scaleX(-1)' } : undefined}
        onMouseMove={onMove}
        onMouseLeave={() => setActive(null)}
        onTouchMove={(e) => onMove(e.touches[0])}
        onTouchEnd={() => setActive(null)}
      >
        <defs>
          <linearGradient id={gradientId} x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stopColor="var(--color-accent)" stopOpacity="0.28" />
            <stop offset="100%" stopColor="var(--color-accent)" stopOpacity="0" />
          </linearGradient>
        </defs>

        <g transform={`translate(0 ${PAD})`}>
          {/* خطوط المحور: أربعة لا شبكة كثيفة — الشبكة تنافس البيانات على الانتباه. */}
          {scale.ticks.map((tick) => {
            const y = HEIGHT - (tick / scale.max) * HEIGHT;
            return <line key={tick} x1="0" x2={WIDTH} y1={y} y2={y} className={styles.grid} />;
          })}

          {!scale.allZero && (
            <>
              <path d={areaPath(coords, HEIGHT)} fill={`url(#${gradientId})`} />
              <path d={linePath(coords)} className={styles.line} />
            </>
          )}

          {current && coords[active] && (
            <>
              <line x1={coords[active].x} x2={coords[active].x} y1="0" y2={HEIGHT} className={styles.marker} />
              <circle cx={coords[active].x} cy={coords[active].y} r="5" className={styles.dot} />
            </>
          )}
        </g>
      </svg>

      {/* القيمة تُعرض خارج الـSVG: نصّ عاديّ يتبع اتجاه الصفحة وخطّها ويُنسَخ. */}
      <p className={styles.readout} aria-hidden="true">
        {current
          ? `${formatLabel(current.label)} · ${formatValue(current.value)}`
          : `${formatLabel(points.at(-1)?.label)} · ${formatValue(points.at(-1)?.value ?? 0)}`}
      </p>
    </div>
  );
}
