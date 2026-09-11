using System.Security.Claims;
using Souq.Application.Common.Security;

namespace Souq.API.Security;

// ============================================================================
// محوّل (Driving Adapter) من مطالبات JWT إلى منفذ ICurrentUser. المكان الوحيد الذي يقرأ
// المطالبات؛ الـ Controllers لا تعرف System.Security.Claims (اختبار معماري). المطالبات
// نفسها التي يكتبها JwtTokenGenerator (MapInboundClaims=false ⇒ الأسماء كما كُتبت)، وقد تحقّقت
// AccessTokenValidation قبل الوصول هنا من مطابقتها للمضيف ومن ختم الأمان.
// ============================================================================
public sealed class HttpCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;
    public HttpCurrentUser(IHttpContextAccessor accessor) => _accessor = accessor;

    private ClaimsPrincipal? Principal =>
        _accessor.HttpContext?.User is { Identity.IsAuthenticated: true } user ? user : null;

    public bool IsAuthenticated => Principal is not null;

    public int? UserId => IntClaim(ClaimTypes.NameIdentifier);

    public int? CustomerId => IntClaim(SouqClaimTypes.CustomerId);

    public IReadOnlyCollection<string> Roles =>
        Principal?.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray() ?? [];

    public bool HasPermission(string permission) => RolePermissions.Grants(Roles, permission);

    private int? IntClaim(string type) =>
        int.TryParse(Principal?.FindFirstValue(type), out var value) ? value : null;
}
