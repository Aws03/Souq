import { useTranslation } from 'react-i18next';
import { ChevronIcon } from '../icons/Icons';
import styles from './Pagination.module.css';

// ترقيم صفحات بسيط يُعاد استخدامه في كل جداول لوحة الإدارة (منتجات/فئات/طلبات).
export default function Pagination({ page, totalPages, onChange }) {
  const { t } = useTranslation();
  // `!(x > 1)` لا `x <= 1`: الثانية تمرّ على `undefined` (كل مقارنة معه خطأ)، فتُبنى ترقيمةٌ
  // بلا نهاية — "التالي" لا يتعطّل أبداً لأن `page >= undefined` خطأ أبداً، والعدّاد بلا رقم.
  // هكذا يصير استدعاءٌ ناقص العدد **غياباً ظاهراً** لا سلوكاً صامتاً خاطئاً.
  if (!(totalPages > 1)) return null;

  // "السابق" نحو بداية القراءة و"التالي" نحو نهايتها — والسهم نفسه يتبع الاتجاه (ChevronIcon).

  return (
    <div className={styles.pagination}>
      <button disabled={page <= 1} onClick={() => onChange(page - 1)}>
        <ChevronIcon dir="start" size={14} /> {t('common.previous')}
      </button>
      <span className={styles.info}>{t('common.pageInfo', { page, totalPages })}</span>
      <button disabled={page >= totalPages} onClick={() => onChange(page + 1)}>
        {t('common.next')} <ChevronIcon dir="end" size={14} />
      </button>
    </div>
  );
}
