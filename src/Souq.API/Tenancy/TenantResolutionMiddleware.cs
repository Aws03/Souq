using System.Globalization;
using Microsoft.Extensions.Options;
using Souq.API.Http;
using Souq.Application.Common.Tenancy;

namespace Souq.API.Tenancy;

// ============================================================================
// TenantResolutionMiddleware — يحدّد المتجر من المضيف قبل أي منطق آخر (ADR-0006، MultiTenancy.md §3).
//   مضيف المنصّة            ⇒ نطاق Platform (لا متجر).
//   نطاق مسجَّل لمتجر       ⇒ ذلك المتجر (Host → TenantDomains، مخزَّن مؤقتاً).
//   تطوير/اختبار فقط       ⇒ X-Tenant، ثم localhost ⇒ المتجر الافتراضي المحلي، ثم {slug}.localhost.
//   غير ذلك                 ⇒ 404 StoreNotFound — لا متجر احتياطي في الإنتاج أبداً.
// لا شيء من جسم الطلب أو سلسلة الاستعلام يشارك في القرار. المضيف لا يمنح إلا بيانات متجره
// العامة، والتوكن يجب أن يطابقه (TenantTokenBinding) — فتزويره لا يكسب شيئاً.
// يعمل على /api و/uploads فقط؛ ملفات متجر لا تُخدَم على مضيف متجر آخر.
// ============================================================================
public sealed class TenantResolutionMiddleware
{
    private const string LocalhostSuffix = ".localhost";

    private readonly RequestDelegate _next;
    private readonly TenancyOptions _options;
    private readonly ILogger<TenantResolutionMiddleware> _logger;

    public TenantResolutionMiddleware(
        RequestDelegate next, IOptions<TenancyOptions> options, ILogger<TenantResolutionMiddleware> logger)
    {
        _next = next; _options = options.Value; _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, TenantContext tenantContext, ITenantDirectory directory)
    {
        if (!IsTenantAware(context.Request.Path))
        {
            await _next(context);
            return;
        }

        var host = context.Request.Host.Host.TrimEnd('.').ToLowerInvariant();
        if (_options.PlatformHosts.Contains(host, StringComparer.OrdinalIgnoreCase))
        {
            tenantContext.UsePlatform();
            await _next(context);
            return;
        }

        var tenant = await ResolveAsync(context, host, directory);
        if (tenant is null)
        {
            _logger.LogDebug("No store is mapped to host {Host}", host);
            await ProblemResponses.WriteAsync(context, StatusCodes.Status404NotFound, "StoreNotFound",
                "لا يوجد متجر على هذا العنوان.");
            return;
        }

        tenantContext.UseTenant(tenant);

        if (IsAnotherTenantsUpload(context.Request.Path, tenant.Id))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await _next(context);
    }

    private async Task<TenantInfo?> ResolveAsync(HttpContext context, string host, ITenantDirectory directory)
    {
        var ct = context.RequestAborted;

        if (_options.AllowDevelopmentResolution
            && context.Request.Headers.TryGetValue(TenancyOptions.DevelopmentTenantHeader, out var header)
            && !string.IsNullOrWhiteSpace(header))
            return await directory.FindBySlugAsync(header.ToString(), ct);

        var mapped = await directory.FindByHostAsync(host, ct);
        if (mapped is not null || !_options.AllowDevelopmentResolution)
            return mapped;

        if (IsLoopback(host))
            return string.IsNullOrWhiteSpace(_options.LocalDefaultTenant)
                ? null
                : await directory.FindBySlugAsync(_options.LocalDefaultTenant, ct);

        return host.EndsWith(LocalhostSuffix, StringComparison.Ordinal)
            ? await directory.FindBySlugAsync(host[..^LocalhostSuffix.Length], ct)
            : null;
    }

    private static bool IsTenantAware(PathString path) =>
        path.StartsWithSegments("/api") || path.StartsWithSegments("/uploads");

    private static bool IsLoopback(string host) => host is "localhost" or "127.0.0.1" or "::1" or "[::1]";

    // /uploads/tenants/{id}/… يُخدَم على مضيف متجره فقط (MultiTenancy.md §4 — مفاتيح التخزين).
    private static bool IsAnotherTenantsUpload(PathString path, int tenantId)
    {
        if (!path.StartsWithSegments("/uploads/tenants", out var rest)) return false;
        var owner = (rest.Value ?? "").TrimStart('/').Split('/', 2)[0];
        return !int.TryParse(owner, NumberStyles.None, CultureInfo.InvariantCulture, out var ownerId) || ownerId != tenantId;
    }
}
