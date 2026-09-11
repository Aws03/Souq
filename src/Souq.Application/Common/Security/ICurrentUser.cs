using Souq.Application.Common.Exceptions;

namespace Souq.Application.Common.Security;

// ============================================================================
// ICurrentUser — منفذ (Port): "من ينفّذ حالة الاستخدام الآن؟" تنفّذه طبقة الـ API من مطالبات التوكن
// (HttpCurrentUser) بعد التحقّق من أنه لهذا المضيف وأن ختم أمانه حالي. حالات الاستخدام تسأل هذا
// المنفذ بدل أن يمرّر الـ Controller المعرّف في الأمر: الهوية لا تأتي من جسم الطلب أبداً، وفحص
// الملكية يعيش في Application لا في Controller (Phase 0 B7, ADR-0019).
// منذ المرحلة 3 الحساب (UserId) منفصل عن ملف الشراء (CustomerId): الطلبات والتقييمات ملك الملف.
// ============================================================================
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    // معرّف حساب الدخول (User) من التوكن؛ null لزائر مجهول.
    int? UserId { get; }

    // ملف العميل في هذا المتجر (مطالبة cid)؛ null لموظّف أو مالك بلا ملف شراء، أو لزائر.
    int? CustomerId { get; }

    IReadOnlyCollection<string> Roles { get; }

    // قرار الصلاحية من الأدوار عبر RolePermissions — لا مقارنة أسماء أدوار في حالات الاستخدام.
    bool HasPermission(string permission);
}

public static class CurrentUserExtensions
{
    // لحالات استخدام لا معنى لها بلا هوية. نقطة بلا [Authorize] بالخطأ ⇒ 401 لا بيانات.
    public static int RequireUserId(this ICurrentUser user) =>
        user.UserId ?? throw new AuthenticationRequiredException();

    // لحالات الشراء (طلب، تقييم، "طلباتي"): زائر ⇒ 401، حساب بلا ملف عميل (موظّف) ⇒ 403.
    public static int RequireCustomerId(this ICurrentUser user)
    {
        if (!user.IsAuthenticated) throw new AuthenticationRequiredException();
        return user.CustomerId ?? throw new CustomerAccountRequiredException();
    }

    // الوصول لمورد يملكه عميل: صاحبه نفسه، أو من يملك صلاحية إدارة هذا النوع. غير ذلك يعامله
    // المستدعي كأنه غير موجود (404 لا 403 — لا نكشف وجود موارد الآخرين).
    public static bool CanAccessOwnedBy(this ICurrentUser user, int ownerCustomerId, string managePermission) =>
        (user.CustomerId is int customerId && customerId == ownerCustomerId) || user.HasPermission(managePermission);
}
