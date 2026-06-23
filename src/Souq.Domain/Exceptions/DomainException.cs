namespace Souq.Domain.Exceptions;

// ============================================================================
// لماذا استثناءات مخصّصة للمجال؟
// عندما تُكسر قاعدة عمل (مثل: طلب كمية أكبر من المتاح)، نريد خطأً يحمل معنىً
// تجارياً واضحاً، لا مجرد Exception عام. هذا يتيح لطبقة الـ API لاحقاً أن
// تترجم كل نوع خطأ إلى رمز HTTP مناسب (مثلاً 400 بدل 500).
// ============================================================================
public abstract class DomainException : Exception
{
    protected DomainException(string message) : base(message) { }
}

// خطأ: الكمية المطلوبة غير متوفرة في المخزون.
public class InsufficientStockException : DomainException
{
    public InsufficientStockException(string productName, int requested, int available)
        : base($"الكمية المطلوبة ({requested}) من \"{productName}\" غير متوفرة. المتاح: {available}") { }
}

// خطأ: محاولة إجراء غير مسموح على طلب حسب حالته الحالية.
public class InvalidOrderOperationException : DomainException
{
    public InvalidOrderOperationException(string message) : base(message) { }
}
