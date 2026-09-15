import { useTranslation } from 'react-i18next';
import Skeleton from '../common/Skeleton';
import { ErrorBanner } from '../common/StateViews';
import styles from './Charts.module.css';

// ============================================================================
// إطار موحّد لكل مخطّط: عنوان، وحالاته الأربع (تحميل، خطأ، لا بيانات، محتوى)، وبديل نصّي.
//
// البديل النصّي ليس زينة إتاحة: مخطّط SVG لا يقرؤه قارئ شاشة، ولا يُقرأ أصلاً في وضع التباين
// العالي أو حين تفشل الخطوط. الجدول المخفي يحمل الأرقام نفسها، فمن لا يرى الرسم يقرأ المعنى
// كاملاً لا عنواناً وحده.
// ============================================================================
export default function ChartFrame({
  title, hint, loading, error, onRetry, isEmpty, emptyMessage, summary, tableRows, children,
}) {
  const { t } = useTranslation();

  return (
    <section className={styles.frame}>
      <header className={styles.frameHead}>
        <div>
          <h3 className={styles.frameTitle}>{title}</h3>
          {hint && <p className={styles.frameHint}>{hint}</p>}
        </div>
      </header>

      {error && <ErrorBanner message={error} onRetry={onRetry} />}

      {!error && loading && <Skeleton height={180} radius={12} />}

      {!error && !loading && isEmpty && (
        <p className={styles.frameEmpty}>{emptyMessage ?? t('admin.reports.noDataYet')}</p>
      )}

      {!error && !loading && !isEmpty && (
        <>
          {/* الرسم نفسه مخفيّ عن شجرة الإتاحة: الجدول أدناه هو نسخته المقروءة. */}
          <div className={styles.frameBody} aria-hidden="true">{children}</div>

          <figure className="souq-visually-hidden">
            {summary && <figcaption>{summary}</figcaption>}
            {tableRows?.length > 0 && (
              <table>
                <caption>{title}</caption>
                <tbody>
                  {tableRows.map((row) => (
                    <tr key={row.label}>
                      <th scope="row">{row.label}</th>
                      <td>{row.value}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </figure>
        </>
      )}
    </section>
  );
}
