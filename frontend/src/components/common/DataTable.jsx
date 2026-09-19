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
 *
 * `label` إلزامي عملياً: هو اسم المنطقة المُمرَّرة، ويُقرأ على قارئ الشاشة قبل الجدول.
 *
 * ── لماذا المنطقة قابلة للتبئير (tabIndex=0) ──
 * الغلاف يمرّر أفقياً (overflow-x: auto) لأنّ جدول لوحة الإدارة أعرض من شاشة الهاتف.
 * ومنطقةٌ تمرّر بلا تبئير لا يصلها من لا يملك فأرة: كروم وسفاري **لا** يمنحان عنصراً
 * غير قابل للتبئير تمريراً بلوحة المفاتيح (فَيَرفُكس وحده يفعل). فكانت أعمدةُ جداول
 * كالتقييمات غير قابلة للوصول أصلاً بلوحة المفاتيح على الهاتف — WCAG 2.1.1، وهو ما
 * تسمّيه axe بـ scrollable-region-focusable.
 *
 * ولم يكشفه أحد لأنّ axe كان يُشغَّل بعرض سطح المكتب وحده، حيث لا يفيض الجدول فلا
 * تمرّر المنطقة أصلاً — فالقاعدة تمرّ بصدق ولا شيء يُقاس. القياس صار بعرض الهاتف.
 */
export default function DataTable({
  columns, rows, rowKey, loading, error, onRetry, label,
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
    // aria-busy أثناء التحميل: هيكلُ التحميل صفوفُ <tr> حقيقية في DOM، لا يميّزها عن صفوف
    // البيانات شيء. فقارئ الشاشة يعلن خمسة صفوف فارغة كأنّها نتيجة، ومن ينتظر الجدول يظنّه
    // وصل. وهو ما وقع فعلاً: رحلةُ الجرد كانت تنتظر أوّل <tr> ثمّ تقرأ — فتقرأ الهيكل، ولا
    // تجد صفّها، فتضغط "التالي" وتتخطّى الصفحة التي كان فيها. والاصطلاح قائم في هذا المستودع
    // أصلاً (Button، BootScreens، StoreSettingsEditor)؛ هذا الجدول وحده كان يغفله.
    <div className={styles.wrap} role="region" aria-label={label} aria-busy={Boolean(loading)} tabIndex={0}>
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
