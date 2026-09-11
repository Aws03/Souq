using System.Security.Claims;
using Souq.Application.Common.Security;

namespace Souq.API.Security;

// ============================================================================
// محوّل (Driving Adapter) من مطالبات JWT إلى منفذ ICurrentUser. المكان الوحيد الذي يقرأ
// المطالبات؛ الـ Controllers لا تعرف System.Security.Claims (اختبار معماري). المطالبات
// نفسها التي يكتبها JwtTokenGenerator (MapInboundClaims=false ⇒ الأسماء كما كُتبت).
// ============================================================================
public sealed class HttpCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;
    public HttpCurrentUser(IHttpContextAccessor accessor) => _accessor = accessor;

    private ClaimsPrincipal? Principal =>
        _accessor.HttpContext?.User is { Identity.IsAuthenticated: true } user ? user : null;

    public bool IsAuthenticated => Principal is not null;

    public int? UserId =>
        int.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public IReadOnlyCollection<string> Roles =>
        Principal?.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray() ?? [];

    public bool HasPermission(string permission) => RolePermissions.Grants(Roles, permission);
}
