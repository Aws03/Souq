using System.Globalization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Souq.Application.Common.Security;
using Souq.Application.Common.Tenancy;

namespace Souq.API.Security;

// ============================================================================
// ربط التوكن بالمضيف (ADR-0006): توكن صادر لمتجر A لا يصلح على مضيف متجر B (ولا على مضيف
// المنصّة) ⇒ المصادقة تفشل ⇒ 401 لكل نقطة محمية. بلا هذا، مدير متجر A يعيد استخدام توكنه على B.
// (المرشّحات تحمي البيانات أصلاً؛ هذا يمنع حتى المحاولة ويجعل الهوية نفسها متّسقة مع المتجر.)
// ============================================================================
public static class TenantTokenBinding
{
    public static Task ValidateAsync(TokenValidatedContext context)
    {
        var tenancy = context.HttpContext.RequestServices.GetRequiredService<ITenantContext>();
        var tokenTenant = context.Principal?.FindFirst(SouqClaimTypes.TenantId)?.Value;

        var matches = tenancy.Scope switch
        {
            TenantScope.Tenant => tokenTenant == tenancy.Tenant!.Id.ToString(CultureInfo.InvariantCulture),
            TenantScope.Platform => tokenTenant is null,
            _ => false,
        };

        if (!matches)
            context.Fail("The token was issued for a different store.");
        return Task.CompletedTask;
    }
}
