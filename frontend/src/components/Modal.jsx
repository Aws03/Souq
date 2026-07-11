// نافذة منبثقة عامة لنماذج الإضافة/التعديل في لوحة الإدارة. تُغلق بالنقر على
// الخلفية أو زر الإغلاق — لا تُغلق أثناء الإرسال (busy) كي لا يُفقد العمل الجاري.
export default function Modal({ title, onClose, busy, children }) {
  return (
    <div className="modal-overlay" onClick={() => !busy && onClose()}>
      <div className="modal-panel" onClick={(e) => e.stopPropagation()}>
        <div className="modal-head">
          <h3>{title}</h3>
          <button type="button" className="modal-close" onClick={onClose} disabled={busy}>✕</button>
        </div>
        <div className="modal-body">{children}</div>
      </div>
    </div>
  );
}
