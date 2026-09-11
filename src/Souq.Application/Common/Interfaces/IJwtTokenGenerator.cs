using Souq.Domain.Identity;

namespace Souq.Application.Common.Interfaces;

// عقد إصدار توكن الوصول (JWT). التنفيذ (المفاتيح، الخوارزمية، المدد) في Infrastructure. المطالبات:
// sub (الحساب)، tid (متجره — لا لحسابات المنصّة)، cid (ملف العميل إن وُجد)، الدور، وختم الأمان.
public interface IJwtTokenGenerator
{
    // مدّة جلسة رمز التجديد — إعداد يملكه المحوّل نفسه (Jwt:RefreshTokenDays).
    TimeSpan RefreshTokenLifetime { get; }

    (string Token, DateTime ExpiresAt) Generate(User user, int? customerId);
}
