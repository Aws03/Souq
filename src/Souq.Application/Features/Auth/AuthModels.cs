using Souq.Application.Common.Security;
using Souq.Domain.Identity;

namespace Souq.Application.Features.Auth;

// ما تراه الواجهة بعد الدخول/التسجيل/التجديد: توكن الوصول القصير ووقت انتهائه وبيانات المستخدم.
// رمز التجديد لا يظهر هنا أبداً — يكتبه الـ Controller في ملف تعريف ارتباط HttpOnly (ADR-0010).
public record AuthResponse(string AccessToken, DateTime ExpiresAt, UserInfo User);

// نتيجة حالة الاستخدام: الاستجابة + رمز التجديد الخام (للـ Controller وحده) وموعد انتهائه.
public sealed record AuthSession(AuthResponse Response, string RefreshToken, DateTime RefreshExpiresAt);

// الحساب كما تعرضه الواجهة. الصلاحيات للعرض فقط (إخفاء ما لا يُسمح به) — الخادم يفرضها دائماً.
public record UserInfo(
    int Id, string FullName, string Email, string Role, IReadOnlyList<string> Permissions,
    bool EmailConfirmed, int? CustomerId, string Area)
{
    public const string StoreArea = "Store";
    public const string PlatformArea = "Platform";

    public static UserInfo From(User user, int? customerId) => new(
        user.Id, user.FullName, user.Email, user.Role,
        RolePermissions.For(user.Role).Order(StringComparer.Ordinal).ToList(),
        user.EmailConfirmedAt is not null, customerId,
        user.BelongsToPlatform ? PlatformArea : StoreArea);
}
