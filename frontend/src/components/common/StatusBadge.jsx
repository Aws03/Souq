import styles from './StatusBadge.module.css';

// ============================================================================
// شارة حالة واحدة لكل التطبيق (TD-28، M10) — الواجهة والإدارة والمنصّة.
//
// النغمة تُحسب في `features/statusTone.js` ولا تُقرَّر هنا: هذا المكوّن يرسم، وذاك يعرف. والفصل
// مقصود — النغمة منطقٌ نقيّ يُختبر بلا DOM، والرسم هو ما يجب أن يكون له مصدرٌ واحد.
//
// `shape` للحالات التي يختلف فيها الشكل لا المعنى: حبّةُ المخزون تحتاج عرضاً أدنى ومحاذاةً وسطى كي
// لا تهتزّ أرقامها بين الصفوف. النغمات نفسها في الحالتين.
// ============================================================================
/**
 * @param {{ tone: import('../../features/statusTone').Tone, children: React.ReactNode,
 *           shape?: 'badge' | 'pill', className?: string, title?: string }} props
 */
export default function StatusBadge({ tone, children, shape = 'badge', className = '', title }) {
  return (
    <span className={`${styles[shape]} ${styles[tone]} ${className}`.trim()} title={title}>
      {children}
    </span>
  );
}
