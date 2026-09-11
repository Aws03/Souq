using Souq.API.Http;
using Souq.API.Security;
using Souq.Application.Common.Tenancy;
using Souq.Domain.Platform;

namespace Souq.API.Tenancy;

// نقطة من منطقة المنصّة: تُخدَم على مضيف المنصّة فقط، وكل ما عداها على مضيف متجر فقط.
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class PlatformEndpointAttribute : Attribute;

// نقطة تعمل والمتجر قيد التجهيز (Provisioning): الدخول، كي تجهّز الإدارة المتجر قبل افتتاحه.
// نقاط الإدارة (HasPermission) مسموحة في التجهيز تلقائياً.
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AvailableDuringProvisioningAttribute : Attribute;

// نقطة تجيب حتى والمتجر موقوف/مؤرشف: إعداد الواجهة، كي تعرض "المتجر غير متاح" بهويّته.
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AvailableWhenStoreClosedAttribute : Attribute;

// ============================================================================
// TenantAvailabilityMiddleware — بعد التوجيه وتحديد المستأجر: هل هذه النقطة متاحة على هذا المضيف
// بهذه الحالة؟ نقطة منصّة على مضيف متجر (أو العكس) ⇒ 404 (لا نكشف وجودها). متجر غير فعّال ⇒ 503
// StoreUnavailable لكل ما لم يُعلَّم صراحةً. قرار واحد في مكان واحد بدل فحص في كل Controller.
// ============================================================================
public sealed class TenantAvailabilityMiddleware
{
    private readonly RequestDelegate _next;
    public TenantAvailabilityMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ITenantContext tenancy)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint is null || tenancy.Scope == TenantScope.None)
        {
            await _next(context);
            return;
        }

        var isPlatformEndpoint = endpoint.Metadata.GetMetadata<PlatformEndpointAttribute>() is not null;
        if (isPlatformEndpoint != (tenancy.Scope == TenantScope.Platform))
        {
            await ProblemResponses.WriteAsync(context, StatusCodes.Status404NotFound, "NotFound", "المورد غير موجود.");
            return;
        }

        if (tenancy.Tenant is { } tenant && !IsOpen(tenant.Status, endpoint))
        {
            await ProblemResponses.WriteAsync(context, StatusCodes.Status503ServiceUnavailable, "StoreUnavailable",
                "المتجر غير متاح حالياً.");
            return;
        }

        await _next(context);
    }

    internal static bool IsOpen(TenantStatus status, Endpoint endpoint) => status switch
    {
        TenantStatus.Active => true,
        TenantStatus.Provisioning => Has<AvailableWhenStoreClosedAttribute>(endpoint)
                                     || Has<AvailableDuringProvisioningAttribute>(endpoint)
                                     || Has<HasPermissionAttribute>(endpoint),
        _ => Has<AvailableWhenStoreClosedAttribute>(endpoint),
    };

    private static bool Has<T>(Endpoint endpoint) where T : class => endpoint.Metadata.GetMetadata<T>() is not null;
}
