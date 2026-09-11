namespace Souq.Application.Common.Models;

// ============================================================================
// نمط Result — لماذا؟
// الأخطاء نوعان: متوقّعة (مخزون غير كافٍ) وغير متوقّعة (انقطاع DB).
// رمي استثناء لكل خطأ متوقّع مكلف ويخلط الأمرين. بدلاً منه نُعيد Result صريحاً
// يحمل إما نجاحاً بقيمة، أو فشلاً بخطأ مصنَّف (Error). هذا يجعل مسارات الخطأ واضحة في
// التوقيع نفسه — القارئ يرى أن العملية قد تفشل دون أن يقرأ الكود الداخلي.
// ============================================================================
// عقد مشترك لنوعَي Result — لمن يرى النتيجة دون معرفة نوع قيمتها (سلوك التدقيق: نجح الطلب أم لا؟).
public interface IResultStatus
{
    bool IsSuccess { get; }
    Error? Error { get; }
}

public class Result<T> : IResultStatus
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public Error? Error { get; }

    // اختصار للقراءة (الاختبارات، السجلات): الرمز الثابت للخطأ إن وُجد.
    public string? ErrorCode => Error?.Code;

    private Result(bool isSuccess, T? value, Error? error)
    {
        IsSuccess = isSuccess; Value = value; Error = error;
    }

    public static Result<T> Success(T value) => new(true, value, null);
    public static Result<T> Failure(Error error) => new(false, default, error);
}

// ============================================================================
// Result (غير معمّم) — لماذا نسخة بلا قيمة؟
// بعض الأوامر تُعدّل الحالة دون أن تُرجع شيئاً ذا معنى (تحديث/حذف منتج). إعادة
// Result<int> وهمي أو Result<bool> يُحمّل المتصل قيمة لا يحتاجها ويُربك القارئ.
// هذه النسخة تعبّر بدقّة عن "نجح/فشل" فقط — بنفس عقد الخطأ (Error) كي تترجمه طبقة
// الـ API بالطريقة الموحّدة نفسها. توسيع طبيعي لا نمط منافس.
// ============================================================================
public class Result : IResultStatus
{
    public bool IsSuccess { get; }
    public Error? Error { get; }
    public string? ErrorCode => Error?.Code;

    private Result(bool isSuccess, Error? error)
    {
        IsSuccess = isSuccess; Error = error;
    }

    public static Result Success() => new(true, null);
    public static Result Failure(Error error) => new(false, error);
}
