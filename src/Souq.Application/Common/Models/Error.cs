namespace Souq.Application.Common.Models;

// ============================================================================
// Error — فشل متوقَّع تقرّره حالة الاستخدام، مصنَّف بنوعه (ADR-0017). قبل 1B كانت طبقة
// الـ API تستنتج رمز HTTP بمطابقة نصوص الرموز ("NotFound"، "EmailTaken"...) في خمسة
// Controllers؛ الآن النوع (Kind) يحدّد الرمز في مكان واحد، والرمز (Code) هو العقد الثابت
// الذي تترجمه الواجهة، والرسالة (Message) للقراءة البشرية فقط وقد تتغيّر.
//
// متى Result.Failure ومتى استثناء؟ نتيجة يقرّرها المعالج بنفسه (غير موجود، تعارض، شرط
// مسبق) ⇒ Error هنا. قاعدة يحرسها الكيان ⇒ DomainException ترتفع وتُترجم مركزياً (422).
// ============================================================================
public enum ErrorKind
{
    Validation,     // 400: مدخل غير صالح الشكل أو يشير إلى ما لا وجود له
    Unauthorized,   // 401: هوية غير مُثبتة أو بيانات دخول خاطئة
    Forbidden,      // 403: هوية مُثبتة بلا صلاحية للإجراء
    NotFound,       // 404: غير موجود — أو يخصّ غيرك (لا نكشف الوجود)
    Conflict,       // 409: يتعارض مع حالة قائمة (قيمة مكرّرة، بيانات تغيّرت منذ قراءتها)
    BusinessRule,   // 422: الطلب سليم لكن قاعدة عمل ترفضه
    Unavailable,    // 503: خدمة خارجية لازمة غير متاحة الآن — أعد المحاولة لاحقاً
}

public sealed record Error(string Code, string Message, ErrorKind Kind)
{
    public static Error Validation(string code, string message) => new(code, message, ErrorKind.Validation);
    public static Error Unauthorized(string code, string message) => new(code, message, ErrorKind.Unauthorized);
    public static Error Forbidden(string code, string message) => new(code, message, ErrorKind.Forbidden);
    public static Error NotFound(string message) => new("NotFound", message, ErrorKind.NotFound);
    public static Error Conflict(string code, string message) => new(code, message, ErrorKind.Conflict);
    public static Error BusinessRule(string code, string message) => new(code, message, ErrorKind.BusinessRule);
    public static Error Unavailable(string code, string message) => new(code, message, ErrorKind.Unavailable);
}
