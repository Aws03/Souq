using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Souq.API.Tenancy;
using Souq.Application.Common.Tenancy;
using Souq.Domain.Platform;

namespace Souq.IntegrationTests;

// ============================================================================
// وسيط تحديد المستأجر وحده (بلا قاعدة): ما يتغيّر بين البيئات لا يُختبر عبر خادم Testing، فنثبت هنا
// أن وسائل التطوير (X-Tenant، localhost، {slug}.localhost) مطفأة تماماً حين لا يسمح بها الإعداد —
// أي في الإنتاج — وأن ملفات متجر لا تُخدَم على مضيف متجر آخر.
// ============================================================================
public class TenantResolutionMiddlewareTests
{
    private static readonly TenantInfo StoreA = new(1, "store-a", "A", TenantStatus.Active, "JOD", "ar", "Asia/Amman", new HashSet<string>(StringComparer.Ordinal));
    private static readonly TenantInfo StoreB = new(2, "store-b", "B", TenantStatus.Active, "USD", "en", "UTC", new HashSet<string>(StringComparer.Ordinal));

    [Fact]
    public async Task الإنتاج_يحدّد_بالنطاق_المسجَّل_ويتجاهل_ترويسة_التطوير()
    {
        var run = await RunAsync("a.example.com", "/api/products", allowDevelopment: false,
            request => request.Headers["X-Tenant"] = "store-b");

        run.Context.Tenant.Should().Be(StoreA);
        run.NextCalled.Should().BeTrue();
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("store-b.localhost")]
    [InlineData("unknown.example.com")]
    public async Task الإنتاج_لا_متجر_احتياطي_لمضيف_غير_مسجَّل(string host)
    {
        var run = await RunAsync(host, "/api/products", allowDevelopment: false);

        run.NextCalled.Should().BeFalse();
        run.Http.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        run.Context.Scope.Should().Be(TenantScope.None);
    }

    [Theory]
    [InlineData("localhost", null, "store-a")]
    [InlineData("store-b.localhost", null, "store-b")]
    [InlineData("localhost", "store-b", "store-b")]
    public async Task التطوير_يقبل_localhost_والنطاق_الفرعي_والترويسة(string host, string? header, string expectedSlug)
    {
        var run = await RunAsync(host, "/api/products", allowDevelopment: true,
            request => { if (header is not null) request.Headers["X-Tenant"] = header; });

        run.Context.Tenant!.Slug.Should().Be(expectedSlug);
    }

    [Fact]
    public async Task مضيف_المنصّة_بلا_متجر()
    {
        var run = await RunAsync("admin.souq.test", "/api/platform/tenants", allowDevelopment: false);

        run.Context.Scope.Should().Be(TenantScope.Platform);
        run.Context.Tenant.Should().BeNull();
        run.NextCalled.Should().BeTrue();
    }

    [Theory]
    [InlineData("/uploads/tenants/2/images/x.png", false)]
    [InlineData("/uploads/tenants/abc/images/x.png", false)]
    [InlineData("/uploads/tenants/1/images/x.png", true)]
    public async Task ملفات_المتجر_على_مضيفه_فقط(string path, bool served)
    {
        var run = await RunAsync("a.example.com", path, allowDevelopment: false);

        run.NextCalled.Should().Be(served);
        if (!served) run.Http.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task المسارات_خارج_الـAPI_لا_تحتاج_متجراً()
    {
        var run = await RunAsync("anything.example", "/swagger/index.html", allowDevelopment: false);

        run.NextCalled.Should().BeTrue();
        run.Context.Scope.Should().Be(TenantScope.None);
    }

    private sealed record Run(TenantContext Context, HttpContext Http, bool NextCalled);

    private static async Task<Run> RunAsync(string host, string path, bool allowDevelopment, Action<HttpRequest>? configure = null)
    {
        var options = Options.Create(new TenancyOptions
        {
            AllowDevelopmentResolution = allowDevelopment,
            LocalDefaultTenant = "store-a",
            PlatformHosts = ["admin.souq.test"],
        });
        var nextCalled = false;
        var middleware = new TenantResolutionMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; }, options, NullLogger<TenantResolutionMiddleware>.Instance);

        var http = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider(),
        };
        http.Request.Host = new HostString(host);
        http.Request.Path = path;
        http.Response.Body = new MemoryStream();
        configure?.Invoke(http.Request);

        var context = new TenantContext();
        await middleware.InvokeAsync(http, context, new FakeDirectory());
        return new Run(context, http, nextCalled);
    }

    private sealed class FakeDirectory : ITenantDirectory
    {
        private static readonly TenantInfo[] Tenants = [StoreA, StoreB];

        public Task<TenantInfo?> FindByHostAsync(string host, CancellationToken ct = default) =>
            Task.FromResult(host == "a.example.com" ? StoreA : null);

        public Task<TenantInfo?> FindBySlugAsync(string slug, CancellationToken ct = default) =>
            Task.FromResult(Tenants.FirstOrDefault(t => t.Slug == slug));

        public Task<TenantInfo?> FindByIdAsync(int tenantId, CancellationToken ct = default) =>
            Task.FromResult(Tenants.FirstOrDefault(t => t.Id == tenantId));

        public Task<IReadOnlyList<TenantInfo>> ListForBackgroundSweepsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TenantInfo>>(Tenants);

        public void Invalidate() { }
    }
}
