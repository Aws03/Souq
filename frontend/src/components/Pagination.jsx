// ترقيم صفحات بسيط يُعاد استخدامه في كل جداول لوحة الإدارة (منتجات/فئات/طلبات).
export default function Pagination({ page, totalPages, onChange }) {
  if (totalPages <= 1) return null;

  return (
    <div className="pagination">
      <button disabled={page <= 1} onClick={() => onChange(page - 1)}>‹ السابق</button>
      <span className="pagination-info">صفحة {page} من {totalPages}</span>
      <button disabled={page >= totalPages} onClick={() => onChange(page + 1)}>التالي ›</button>
    </div>
  );
}
