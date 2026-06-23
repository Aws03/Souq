namespace Souq.Application.Common.Models;

// ============================================================================
// نمط Result — لماذا؟
// الأخطاء نوعان: متوقّعة (مخزون غير كافٍ) وغير متوقّعة (انقطاع DB).
// رمي استثناء لكل خطأ متوقّع مكلف ويخلط الأمرين. بدلاً منه نُعيد Result صريحاً
// يحمل إما نجاحاً بقيمة، أو فشلاً برسالة. هذا يجعل مسارات الخطأ واضحة في التوقيع
// نفسه — القارئ يرى أن العملية قد تفشل دون أن يقرأ الكود الداخلي.
// ============================================================================
public class Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public string? Error { get; }
    public string? ErrorCode { get; }

    private Result(bool isSuccess, T? value, string? error, string? errorCode)
    {
        IsSuccess = isSuccess; Value = value; Error = error; ErrorCode = errorCode;
    }

    public static Result<T> Success(T value) => new(true, value, null, null);
    public static Result<T> Failure(string error, string code = "Error") => new(false, default, error, code);
}
