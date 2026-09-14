using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Souq.Domain.Common;
using Souq.Domain.Identity;
using Souq.Application.Common.Tenancy;
using Souq.Domain.Platform;
using Souq.Infrastructure.Persistence;
using Souq.IntegrationTests.Infrastructure;

namespace Souq.IntegrationTests;

// ============================================================================
// M7 — ما يحصل عليه *قاعدة جديدة بشكل الإنتاج* بالضبط.
//
// بقية اختبارات الإعداد تفحص القرار (ShouldSeedDemoData) مجرّداً. هذا يفحص النتيجة: خادم
// حقيقي يُقلع على قاعدة فارغة تماماً ببذر إنتاجي (Seed:DemoData=false، بلا بيانات مدير)، ثم
// نقرأ ما استقرّ في الجداول. الفرق مهم — قرار صحيح مع مسار بذر خاطئ ينتج قاعدة ملوّثة.
//
// القاعدة تُنشأ وتُحذف داخل الاختبار على خادم الحاويات نفسه: لا تلمس قاعدة المجموعة المشتركة.
// ============================================================================
[Collection(IntegrationCollection.Name)]
public class SeedSafetyTests
{
    private readonly SouqApiFactory _shared;

    public SeedSafetyTests(SouqApiFactory shared) => _shared = shared;

    [Fact]
    public async Task قاعدة_جديدة_بشكل_الإنتاج_لا_تستقبل_كتالوجاً_ولا_مديراً_افتراضياً()
    {
        await using var production = new FreshDatabaseFactory(_shared.ConnectionString, seedDemoData: false);
        production.CreateClient();   // يُقلع: الهجرات + البذر

        await using var scope = production.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<Souq.Application.Common.Tenancy.TenantContext>()
            .UseTenant(await production.DefaultTenantAsync());
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // لا كتالوج عرض: ولا منتج ولا فئة واحدة.
        (await db.Products.IgnoreQueryFilters().CountAsync()).Should().Be(0, "بيانات العرض لا تدخل الإنتاج");
        (await db.Categories.IgnoreQueryFilters().CountAsync()).Should().Be(0);

        // ولا حساب: لا مدير افتراضي منشور، ولا مالك منصّة، لأن أياً منهما لم يُضبط صراحةً.
        (await db.Users.IgnoreQueryFilters().CountAsync()).Should().Be(0, "الحسابات تُنشأ من إعداد صريح فقط");

        // والمتجر الافتراضي موجود (تكتبه الهجرة) لكن بلا مظهر ماركة: البذر لم يلمس إعداداته.
        var tenant = await db.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Slug == DbSeeder.DefaultTenantSlug);
        tenant.HasCustomSettings.Should().BeFalse("مظهر العرض التوضيحي لا يُطبَّق خارج بيانات العرض");
    }

    [Fact]
    public async Task قاعدة_إنتاج_جديدة_تُحذّر_أن_المتجر_الافتراضي_لم_يتبنّه_أحد()
    {
        // الخطر: هجرة المرحلة 2 تكتب متجراً فعّالاً باسم العرض التوضيحي في *كل* قاعدة. حذفه
        // من الهجرة غير ممكن بأثر رجعي، فالضمانة الوحيدة أن يقولها الإقلاع بوضوح.
        await using var production = new FreshDatabaseFactory(_shared.ConnectionString, seedDemoData: false);
        production.CreateClient();

        production.Logs.Messages.Should().Contain(m => m.Contains(DbSeeder.DefaultTenantSeededName),
            "الإقلاع يسمّي المتجر غير المتبنَّى");
        production.Logs.Messages.Should().Contain(m => m.Contains("بلا أي مدير"),
            "ومتجر فعّال بلا مدير لا يديره أحد");
    }

    [Fact]
    public async Task بيانات_العرض_تدخل_فقط_حين_تُطلب_صراحةً()
    {
        await using var demo = new FreshDatabaseFactory(_shared.ConnectionString, seedDemoData: true);
        demo.CreateClient();

        await using var scope = demo.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<Souq.Application.Common.Tenancy.TenantContext>()
            .UseTenant(await demo.DefaultTenantAsync());
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        (await db.Products.IgnoreQueryFilters().CountAsync()).Should().BeGreaterThan(0);
        (await db.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Slug == DbSeeder.DefaultTenantSlug))
            .HasCustomSettings.Should().BeTrue();
        // وحتى هنا: لا مدير بكلمة مرور منشورة، لأن البيئة ليست Development.
        (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == DbSeeder.DevelopmentAdminEmail)).Should().BeFalse();
    }

    [Fact]
    public async Task المدير_الأول_في_الإنتاج_يُنشأ_من_إعداد_صريح_وحده()
    {
        await using var production = new FreshDatabaseFactory(_shared.ConnectionString, seedDemoData: false,
            ("Seed:AdminEmail", "owner@store.example"), ("Seed:AdminPassword", "Bootstrap-Owner-2026!"));
        production.CreateClient();

        await using var scope = production.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<Souq.Application.Common.Tenancy.TenantContext>()
            .UseTenant(await production.DefaultTenantAsync());
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var admins = await db.Users.IgnoreQueryFilters().Where(u => u.Role == Roles.TenantAdmin).ToListAsync();
        admins.Should().ContainSingle().Which.Email.Should().Be("owner@store.example");
        // والتحذير عن متجر بلا مدير لم يعد ينطبق.
        production.Logs.Messages.Should().NotContain(m => m.Contains("بلا أي مدير"));
    }

    // ── خادم كامل على قاعدة جديدة تُحذف بعد الاختبار ─────────────────────────────
    private sealed class FreshDatabaseFactory : WebApplicationFactory<Program>, IAsyncDisposable
    {
        private readonly string _connectionString;
        private readonly bool _seedDemoData;
        private readonly (string Key, string Value)[] _settings;

        public CapturingLoggerProvider Logs { get; } = new();

        public FreshDatabaseFactory(string serverConnectionString, bool seedDemoData,
            params (string Key, string Value)[] settings)
        {
            _connectionString = new SqlConnectionStringBuilder(serverConnectionString)
            {
                InitialCatalog = $"seed_{Guid.NewGuid():N}"[..24],
            }.ConnectionString;
            _seedDemoData = seedDemoData;
            _settings = settings;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Default", _connectionString);
            builder.UseSetting("Jwt:Key", "seed-safety-tests-signing-key-0123456789abcdef0123456789");
            builder.UseSetting("Seed:DemoData", _seedDemoData ? "true" : "false");
            builder.UseSetting("Inventory:SweepIntervalSeconds", "0");
            builder.UseSetting("Basket:CleanupIntervalMinutes", "0");
            builder.UseSetting("Notifications:DispatchIntervalSeconds", "0");
            foreach (var (key, value) in _settings) builder.UseSetting(key, value);
            builder.ConfigureLogging(logging => logging.AddProvider(Logs));
        }

        public async Task<TenantInfo> DefaultTenantAsync()
        {
            await using var scope = Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<ITenantDirectory>()
                .FindBySlugAsync(DbSeeder.DefaultTenantSlug)
                ?? throw new InvalidOperationException("المتجر الافتراضي غير موجود");
        }

        public new async ValueTask DisposeAsync()
        {
            try
            {
                await using var scope = Services.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureDeletedAsync();
            }
            catch (Exception) { /* القاعدة لم تُنشأ أصلاً */ }
            await base.DisposeAsync();
        }
    }
}
