import { useTranslation } from 'react-i18next';
import { ChevronIcon } from '../icons/Icons';
import styles from './Pagination.module.css';

// ترقيم صفحات بسيط يُعاد استخدامه في كل جداول لوحة الإدارة (منتجات/فئات/طلبات).
export default function Pagination({ page, totalPages, onChange }) {
  const { t, i18n } = useTranslation();
  if (totalPages <= 1) return null;

  // اتجاه السهم منطقي لا ثابت: يشير "السابق"/"Previous" دوماً نحو بداية
  // اتجاه القراءة (يميناً في RTL، يساراً في LTR)، والعكس لـ"التالي"/"Next".
  const isRtl = i18n.dir() === 'rtl';
  const prevDir = isRtl ? 'end' : 'start';
  const nextDir = isRtl ? 'start' : 'end';

  return (
    <div className={styles.pagination}>
      <button disabled={page <= 1} onClick={() => onChange(page - 1)}>
        <ChevronIcon dir={prevDir} size={14} /> {t('common.previous')}
      </button>
      <span className={styles.info}>{t('common.pageInfo', { page, totalPages })}</span>
      <button disabled={page >= totalPages} onClick={() => onChange(page + 1)}>
        {t('common.next')} <ChevronIcon dir={nextDir} size={14} />
      </button>
    </div>
  );
}
