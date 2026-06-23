using Souq.Domain.Common;

namespace Souq.Domain.Interfaces;

// ============================================================================
// IRepository<T> — واجهة معمّمة للوصول للبيانات (نمط Repository).
//
// لماذا واجهة وليست تنفيذاً؟ هنا جوهر "عكس التبعية":
//   - طبقة Domain تقول: "أنا أحتاج طريقة لجلب وحفظ الكيانات". (هذه الواجهة)
//   - طبقة Infrastructure تقول: "أنا سأنفّذها باستخدام EF Core و SQL Server".
//
// النتيجة: Domain لا يعرف ولا يهتم بـ SQL إطلاقاً. لو بدّلنا قاعدة البيانات
// كلها غداً، لا يتغيّر حرف واحد في Domain. السهم يشير للداخل دائماً.
//
// لماذا معمّم <T>؟ لئلا نكتب نفس دوال (جلب/إضافة/حذف) لكل كيان على حدة (DRY).
// ============================================================================
public interface IRepository<T> where T : Entity
{
    Task<T?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<T>> ListAllAsync(CancellationToken ct = default);
    Task AddAsync(T entity, CancellationToken ct = default);
    void Update(T entity);
    void Remove(T entity);
}
