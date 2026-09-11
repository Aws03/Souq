using Microsoft.Extensions.DependencyInjection;
using Souq.Application.Common.Tenancy;

namespace Souq.Infrastructure.Tenancy;

// ============================================================================
// TenantScopes — العمل داخل متجر بعينه خارج طلب HTTP: البذر، المهام الخلفية (تمرّ على المتاجر
// واحداً واحداً)، وعمليات المنصّة التي تكتب في متجر محدَّد. نطاق خدمات جديد بكامله — DbContext
// جديد وسياق جديد مضبوط على المتجر — لا "تبديل" سياق قائم (TenantContext يُضبط مرة واحدة).
// ============================================================================
public static class TenantScopes
{
    public static async Task RunAsync(IServiceProvider services, TenantInfo tenant, Func<IServiceProvider, Task> work)
    {
        await using var scope = services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().UseTenant(tenant);
        await work(scope.ServiceProvider);
    }

    public static async Task<T> RunAsync<T>(IServiceProvider services, TenantInfo tenant, Func<IServiceProvider, Task<T>> work)
    {
        await using var scope = services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().UseTenant(tenant);
        return await work(scope.ServiceProvider);
    }

    // نطاق المنصّة خارج طلب HTTP (المرحلة 14): رسائل حسابات المنصّة في صندوق الصادر — حساباتها وحدها مرئية فيه.
    public static async Task RunPlatformAsync(IServiceProvider services, Func<IServiceProvider, Task> work)
    {
        await using var scope = services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().UsePlatform();
        await work(scope.ServiceProvider);
    }
}
