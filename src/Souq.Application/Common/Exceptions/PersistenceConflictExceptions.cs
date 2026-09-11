namespace Souq.Application.Common.Exceptions;

// ============================================================================
// أخطاء تعارض الحفظ — تُعرَّف هنا (Application) وترميها وحدة العمل في Infrastructure
// بعد ترجمة استثناءات EF/SQL Server، فلا يتسرّب أي نوع تقني خارج طبقة التنفيذ
// (ADR-0003). طبقة الـ API تترجم كليهما إلى 409 Conflict.
// ============================================================================

// السجل تغيّر منذ قراءته (rowversion) — كتابتان متزامنتان على نفس التجمّع (ADR-0013).
public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException(Exception? inner = null)
        : base("تغيّرت البيانات أثناء تنفيذ العملية. أعد المحاولة بعد تحديث الصفحة.", inner) { }
}

// قيد فريد في قاعدة البيانات رفض القيمة — عادةً سباق بين طلبين تجاوزا الفحص المبكر معاً.
public sealed class UniqueConstraintViolationException : Exception
{
    public UniqueConstraintViolationException(Exception? inner = null)
        : base("القيمة مستخدمة مسبقاً.", inner) { }
}
