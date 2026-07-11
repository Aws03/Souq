import { ChevronIcon } from '../icons/Icons';
import styles from './Pagination.module.css';

// ترقيم صفحات بسيط يُعاد استخدامه في كل جداول لوحة الإدارة (منتجات/فئات/طلبات).
export default function Pagination({ page, totalPages, onChange }) {
  if (totalPages <= 1) return null;

  return (
    <div className={styles.pagination}>
      <button disabled={page <= 1} onClick={() => onChange(page - 1)}>
        <ChevronIcon dir="end" size={14} /> السابق
      </button>
      <span className={styles.info}>صفحة {page} من {totalPages}</span>
      <button disabled={page >= totalPages} onClick={() => onChange(page + 1)}>
        التالي <ChevronIcon dir="start" size={14} />
      </button>
    </div>
  );
}
