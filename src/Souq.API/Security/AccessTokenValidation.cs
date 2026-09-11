using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Souq.Application.Common.Security;
using Souq.Application.Common.Tenancy;

namespace Souq.API.Security;

// ============================================================================
// ما يُفحص بعد صحّة توقيع التوكن وعمره (JwtBearerEvents.OnTokenValidated) — فشل أيٍّ منهما ⇒ التوكن
// غير صالح ⇒ 401 لكل نقطة محمية:
//   1) الربط بالمضيف (ADR-0006): tid يطابق متجر المضيف؛ على مضيف المنصّة لا tid إطلاقاً. بلا هذا،
//      مدير متجر A يعيد استخدام توكنه على B (المرشّحات تحمي البيانات أصلاً؛ هذا يمنع حتى المحاولة).
//   2) ختم الأمان (ADR-0010): sstamp يطابق ختم الحساب الحالي والحساب فعّال — تغيير كلمة المرور،
//      التعطيل، أو كشف سرقة رمز تجديد يُسقط كل توكنات الوصول فوراً لا بعد انتهائها.
// ============================================================================
public static class AccessTokenValidation
{
    public static async Task ValidateAsync(TokenValidatedContext context)
    {
        var principal = context.Principal;
        var services = context.HttpContext.RequestServices;
        var tenancy = services.GetRequiredService<ITenantContext>();

        var tokenTenant = principal?.FindFirst(SouqClaimTypes.TenantId)?.Value;
        var boundToHost = tenancy.Scope switch
        {
            TenantScope.Tenant => tokenTenant == tenancy.Tenant!.Id.ToString(CultureInfo.InvariantCulture),
            TenantScope.Platform => tokenTenant is null,
            _ => false,
        };
        if (!boundToHost)
        {
            context.Fail("The token was issued for a different store.");
            return;
        }

        var stamp = principal?.FindFirst(SouqClaimTypes.SecurityStamp)?.Value;
        var current = int.TryParse(principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId)
                      && !string.IsNullOrEmpty(stamp)
                      && await services.GetRequiredService<ISessionValidator>()
                          .IsCurrentAsync(userId, stamp, context.HttpContext.RequestAborted);
        if (!current)
            context.Fail("The session is no longer valid.");
    }
}
