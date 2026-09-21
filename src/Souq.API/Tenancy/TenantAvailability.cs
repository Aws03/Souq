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
// نقطة تجيب والمتجر **موقوف** ولا تجيب وهو **مؤرشف** — وهذا هو التمييز الذي لم يكن موجوداً.
//
// قرار المالك C-17 = B (2026-09-21): الإيقاف يُغلق الواجهة ويُبقي الإدارة، و«يبقى العميل قادراً
// على تتبّع طلبٍ دفع ثمنه». والإيقاف مؤقّت لسببٍ بين المنصّة والتاجر — لا شأن للمشتري به، فحجب
// طلبٍ دفع ثمنه عنه عقوبةٌ على غير المخطئ.
//
// والأرشفة نهائية: المتجر لا يعود إلى الخدمة، فلا إدارة ولا تتبّع. قبل هذا كانت الحالتان فرعاً
// واحداً (`_ =>`) فلم يكن بينهما فرق أصلاً.
// ============================================================================
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AvailableWhenStoreSuspendedAttribute : Attribute;

// نقطة تُخدَم على مضيف المنصّة ومضيفي المتاجر معاً (الدخول وجلساته): سلوكها يتبع النطاق —
// حسابات المتجر على مضيفه، وحسابات المنصّة على مضيفها (المستودعات مُرشَّحة بالنطاق).
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AvailableOnAllHostsAttribute : Attribute;

// نقطة من وحدة اختيارية (D-11): معطّلة لمتجر المضيف ⇒ 404 ModuleDisabled قبل المصادقة وأي منطق. الواجهة
// تُخفي الوحدة من إعدادها، وهذا هو الفرض الفعلي (وحالات الاستخدام التي تمسّ الوحدة تفحص أيضاً).
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequiresModuleAttribute(string module) : Attribute
{
    public string Module { get; } = module;
}

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
        if (!Has<AvailableOnAllHostsAttribute>(endpoint) && isPlatformEndpoint != (tenancy.Scope == TenantScope.Platform))
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

        if (tenancy.Tenant is { } store && endpoint.Metadata.GetMetadata<RequiresModuleAttribute>() is { } required
            && !store.HasModule(required.Module))
        {
            await ProblemResponses.WriteAsync(context, StatusCodes.Status404NotFound, "ModuleDisabled",
                "هذه الميزة غير مفعّلة في هذا المتجر.");
            return;
        }

        await _next(context);
    }

    // ============================================================================
    // ما يبقى مفتوحاً بكل حالة. المتجر الفعّال مفتوح، وما عداه مغلق إلا ما عُلِّم صراحةً.
    //
    // **الموقوف والمؤرشف كانا فرعاً واحداً** حتى قرار C-17 = B، فكان الإيقاف والأرشفة سواءً لكل
    // مُنادٍ. الآن:
    //   • الموقوف: نقاط الإدارة (HasPermission) تعمل — فالتاجر يدخل ويُصلح سبب الإيقاف، وهذا هو
    //     معنى «إدارة فقط» — وتتبّعُ الطلبات المدفوعة يبقى، والواجهة تُغلق. والشراء يُرفض عند
    //     الخادم لا في المتصفّح وحده: مسارات الشراء ليست محروسة بصلاحية، فتسقط في هذا الفرع
    //     بالبناء لا بالسهو.
    //   • المؤرشف: كما كان — إعداد الواجهة والدخول وحدهما (وهو ما يُظهر شاشة الإغلاق بهويّة
    //     المتجر بدل صفحة خطأ عارية).
    // ============================================================================
    internal static bool IsOpen(TenantStatus status, Endpoint endpoint) => status switch
    {
        TenantStatus.Active => true,
        TenantStatus.Provisioning => Has<AvailableWhenStoreClosedAttribute>(endpoint)
                                     || Has<AvailableDuringProvisioningAttribute>(endpoint)
                                     || Has<HasPermissionAttribute>(endpoint),
        TenantStatus.Suspended => Has<AvailableWhenStoreClosedAttribute>(endpoint)
                                  || Has<AvailableWhenStoreSuspendedAttribute>(endpoint)
                                  || Has<HasPermissionAttribute>(endpoint),
        _ => Has<AvailableWhenStoreClosedAttribute>(endpoint),
    };

    private static bool Has<T>(Endpoint endpoint) where T : class => endpoint.Metadata.GetMetadata<T>() is not null;
}
