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

// خطأ: قيمة غير صالحة لبيانات المنتج (مثل مخزون سالب). كونه DomainException
// يضمن أن تترجمه طبقة الـ API إلى 400 (خطأ عميل) لا 500 (خطأ خادم).
public class InvalidProductDataException : DomainException
{
    public InvalidProductDataException(string message) : base(message) { }
}

// خطأ: كوبون غير صالح للاستخدام الآن (منتهٍ، معطّل، مستنفَد، أو الطلب لا يبلغ
// الحد الأدنى) أو بيانات إنشاء كوبون غير منطقية (نسبة خصم خارج 1-100 مثلاً).
public class InvalidCouponException : DomainException
{
    public InvalidCouponException(string message) : base(message) { }
}

// خطأ: بيانات تقييم غير صالحة (تقييم خارج 1-5، تعليق فارغ أو طويل جداً).
public class InvalidReviewException : DomainException
{
    public InvalidReviewException(string message) : base(message) { }
}

// خطأ: رمز إعادة تعيين كلمة المرور منتهي الصلاحية (أُنشئ منذ أكثر من ساعتين).
public class InvalidPasswordResetException : DomainException
{
    public InvalidPasswordResetException(string message) : base(message) { }
}
