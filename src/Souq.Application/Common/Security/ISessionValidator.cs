namespace Souq.Application.Common.Security;

// ============================================================================
// ISessionValidator — هل ما زال توكن الوصول صالحاً لحسابه؟ التوكن يحمل ختم الأمان (sstamp) الذي
// يتغيّر مع كل ما يُبطل الجلسات (كلمة مرور، تعطيل، دور، سرقة رمز تجديد). التنفيذ في Infrastructure
// يخزّن الختم ثوانيَ معدودة كي لا يُسأل SQL مع كل طلب؛ Forget يُسقطه فوراً على هذه النسخة بعد التدوير.
// ============================================================================
public interface ISessionValidator
{
    Task<bool> IsCurrentAsync(int userId, string securityStamp, CancellationToken ct = default);

    void Forget(int userId);
}
