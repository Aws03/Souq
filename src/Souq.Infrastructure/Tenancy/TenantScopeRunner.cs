using Microsoft.Extensions.DependencyInjection;
using Souq.Application.Common.Tenancy;

namespace Souq.Infrastructure.Tenancy;

// تنفيذ ITenantScopeRunner فوق TenantScopes: نطاق خدمات جديد بسياق المتجر المستهدف، والخدمة المطلوبة منه.
internal sealed class TenantScopeRunner : ITenantScopeRunner
{
    private readonly IServiceProvider _services;
    public TenantScopeRunner(IServiceProvider services) => _services = services;

    public Task<TResult> RunAsync<TService, TResult>(TenantInfo tenant, Func<TService, Task<TResult>> work)
        where TService : notnull =>
        TenantScopes.RunAsync(_services, tenant, provider => work(provider.GetRequiredService<TService>()));
}
