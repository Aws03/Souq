using Souq.Domain.Common;

namespace Souq.Domain.Interfaces;

// ============================================================================
// IRepository<T> — منفذ الكتابة لتجمّع (نمط Repository).
//
// لماذا واجهة وليست تنفيذاً؟ جوهر "عكس التبعية": Domain يقول "أحتاج جلب وحفظ الكيانات"،
// وInfrastructure ينفّذها بـ EF Core. Domain لا يعرف SQL إطلاقاً. السهم يشير للداخل دائماً.
//
// ما ليس هنا عمداً (Phase 1B):
//   Update — الكيان المُحمَّل عبر المستودع متتبَّع؛ وحدة العمل تكتشف ما تغيّر وتكتب الأعمدة
//            المتغيّرة فقط. Update() كان يعلّم كل الأعمدة معدّلة فيكتب فوق تغييرات متزامنة
//            على أعمدة لم نلمسها (Phase 0 D7).
//   ListAllAsync — تحميل جدول كامل لأي تجمّع كان فخّاً ينمو مع البيانات. القراءات للعرض تمرّ
//            عبر خدمات القراءة (ICatalogQueries…) بإسقاط وترقيم (ADR-0008).
// ============================================================================
public interface IRepository<T> where T : Entity
{
    Task<T?> GetByIdAsync(int id, CancellationToken ct = default);
    Task AddAsync(T entity, CancellationToken ct = default);
    void Remove(T entity);
}
