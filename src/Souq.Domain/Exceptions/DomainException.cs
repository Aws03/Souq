namespace Souq.Domain.Exceptions;

// ============================================================================
// لماذا استثناءات مخصّصة للمجال؟
// عندما تُكسر قاعدة عمل (مثل: طلب كمية أكبر من المتاح)، نريد خطأً يحمل معنىً
// تجارياً واضحاً، لا مجرد Exception عام. طبقة الـ API تترجم أي DomainException إلى
// 422 مع رمزه (Code) — الرمز عقد ثابت تترجمه الواجهة، والرسالة للقراءة فقط (ADR-0017).
// المعالجات لا تلتقط هذه الاستثناءات: قاعدة واحدة، مسار واحد (Phase 0 D6).
// ============================================================================
public abstract class DomainException : Exception
{
    public string Code { get; }

    protected DomainException(string code, string message) : base(message) => Code = code;
}

// خطأ: الكمية المطلوبة غير متوفرة في المخزون.
public class InsufficientStockException : DomainException
{
    public InsufficientStockException(string productName, int requested, int available)
        : base("InsufficientStock", $"الكمية المطلوبة ({requested}) من \"{productName}\" غير متوفرة. المتاح: {available}") { }
}

// خطأ: محاولة إجراء غير مسموح على طلب حسب حالته الحالية.
public class InvalidOrderOperationException : DomainException
{
    public InvalidOrderOperationException(string message) : base("InvalidOrderOperation", message) { }
}

// خطأ: قيمة غير صالحة لبيانات المنتج (مثل مخزون سالب).
public class InvalidProductDataException : DomainException
{
    public InvalidProductDataException(string message) : base("InvalidProductData", message) { }
}

// خطأ: كوبون غير صالح للاستخدام الآن (منتهٍ، معطّل، مستنفَد، أو الطلب لا يبلغ
// الحد الأدنى) أو بيانات إنشاء كوبون غير منطقية (نسبة خصم خارج 1-100 مثلاً).
public class InvalidCouponException : DomainException
{
    public InvalidCouponException(string message) : base("InvalidCoupon", message) { }
}

// خطأ: بيانات تقييم غير صالحة (تقييم خارج 1-5، تعليق فارغ أو طويل جداً).
public class InvalidReviewException : DomainException
{
    public InvalidReviewException(string message) : base("InvalidReview", message) { }
}

// خطأ: رمز إعادة تعيين كلمة المرور منتهي الصلاحية أو لا طلب إعادة تعيين قائم.
public class InvalidPasswordResetException : DomainException
{
    public InvalidPasswordResetException(string message) : base("ResetTokenExpired", message) { }
}

// خطأ: مبلغ مالي غير صالح (سالب، عملة غير صالحة، خانات عشرية أكثر مما تسمح به
// العملة، أو عملتان مختلفتان في عملية واحدة).
public class InvalidMoneyException : DomainException
{
    public InvalidMoneyException(string message) : base("InvalidMoney", message) { }
}

// خطأ: بيانات حساب أو عملية هوية غير صالحة (بريد، اسم، دور، نقل حساب بين المنصّة والمتجر).
public class InvalidIdentityOperationException : DomainException
{
    public InvalidIdentityOperationException(string message) : base("InvalidIdentityOperation", message) { }
}

// خطأ: رمز تأكيد البريد منتهي الصلاحية أو لا طلب تأكيد قائم.
public class InvalidEmailVerificationException : DomainException
{
    public InvalidEmailVerificationException(string message) : base("VerificationTokenExpired", message) { }
}

// خطأ: عملية غير مسموحة على متجر (انتقال حالة غير صالح، نطاق/معرّف/عملة غير صالحة).
public class InvalidTenantOperationException : DomainException
{
    public InvalidTenantOperationException(string message) : base("InvalidTenantOperation", message) { }
}
