using Souq.Application.Common.Exceptions;

namespace Souq.Application.Common.Security;

// ============================================================================
// ICurrentUser — منفذ (Port): "من ينفّذ حالة الاستخدام الآن؟" تنفّذه طبقة الـ API من
// مطالبات التوكن (HttpCurrentUser). حالات الاستخدام تسأل هذا المنفذ بدل أن يمرّر الـ
// Controller المعرّف في الأمر: الهوية لا تأتي من جسم الطلب أبداً، وفحص الملكية يعيش في
// Application لا في Controller (Phase 0 B7, ADR-0019).
// المرحلة 2 تضيف TenantId (من مطالبة tid المطابقة للمضيف)؛ المرحلة 3 تفصل User عن Customer.
// ============================================================================
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    // معرّف الحساب من التوكن؛ null لزائر مجهول.
    int? UserId { get; }

    IReadOnlyCollection<string> Roles { get; }

    // قرار الصلاحية من الأدوار عبر RolePermissions — لا مقارنة أسماء أدوار في حالات الاستخدام.
    bool HasPermission(string permission);
}

public static class CurrentUserExtensions
{
    // لحالات استخدام لا معنى لها بلا هوية. نقطة بلا [Authorize] بالخطأ ⇒ 401 لا بيانات.
    public static int RequireUserId(this ICurrentUser user) =>
        user.UserId ?? throw new AuthenticationRequiredException();

    // الوصول لمورد يملكه مستخدم: المالك نفسه، أو من يملك صلاحية إدارة هذا النوع. غير ذلك
    // يعامله المستدعي كأنه غير موجود (404 لا 403 — لا نكشف وجود موارد الآخرين).
    public static bool CanAccessOwnedBy(this ICurrentUser user, int ownerId, string managePermission) =>
        user.UserId == ownerId || user.HasPermission(managePermission);
}
