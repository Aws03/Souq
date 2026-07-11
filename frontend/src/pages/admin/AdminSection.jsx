// حاجز مؤقت لأقسام الإدارة التي تُبنى بالكامل في المرحلة 3ب (نماذج المنتجات
// والفئات وشاشة الطلبات). ليس تصميماً نهائياً بل إعلان صريح بأن القسم قادم —
// كي لا تكون روابط الشريط الجانبي معطّلة. يُستبدل محتواه في 3ب.
export default function AdminSection({ title }) {
  return (
    <div>
      <h2 className="admin-page-title">{title}</h2>
      <div className="admin-soon">
        <div className="admin-soon-badge">قيد الإنشاء</div>
        <p>يُبنى هذا القسم بالكامل (نماذج الإضافة/التعديل ورفع الصور) في المرحلة القادمة.</p>
      </div>
    </div>
  );
}
