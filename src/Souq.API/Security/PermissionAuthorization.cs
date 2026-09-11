using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Souq.Application.Common.Security;

namespace Souq.API.Security;

// ============================================================================
// تفويض بالصلاحيات (ADR-0019): [HasPermission(Permissions.Orders.Manage)] بدل
// [Authorize(Roles = "Admin")]. السياسة تُبنى من اسمها عند الطلب (لا تسجيل يدوي لكل
// صلاحية)، والقرار من RolePermissions — نفس الجدول الذي تسأله حالات الاستخدام.
//   زائر ⇒ 401 (السياسة تتطلّب مستخدماً مُصادَقاً)، مُصادَق بلا الصلاحية ⇒ 403.
// ============================================================================
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class HasPermissionAttribute : AuthorizeAttribute
{
    public const string PolicyPrefix = "permission:";

    public HasPermissionAttribute(string permission) : base(PolicyPrefix + permission) => Permission = permission;

    public string Permission { get; }
}

public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

public sealed class PermissionPolicyProvider : DefaultAuthorizationPolicyProvider
{
    private readonly ConcurrentDictionary<string, AuthorizationPolicy> _policies = new(StringComparer.Ordinal);

    public PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : base(options) { }

    public override Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(HasPermissionAttribute.PolicyPrefix, StringComparison.Ordinal))
            return base.GetPolicyAsync(policyName);

        var permission = policyName[HasPermissionAttribute.PolicyPrefix.Length..];
        // خطأ إملائي في اسم صلاحية = خطأ برمجي صاخب (500 + اختبار يفشل)، لا 403 صامت للجميع.
        if (!Permissions.All.Contains(permission))
            throw new InvalidOperationException($"صلاحية غير معرّفة في Permissions: {permission}");

        return Task.FromResult<AuthorizationPolicy?>(_policies.GetOrAdd(permission, p =>
            new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(p))
                .Build()));
    }
}

public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var roles = context.User.FindAll(ClaimTypes.Role).Select(c => c.Value);
        if (RolePermissions.Grants(roles, requirement.Permission))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
