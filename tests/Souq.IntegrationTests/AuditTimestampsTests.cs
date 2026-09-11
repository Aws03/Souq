using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Souq.Domain.Entities;
using Souq.Infrastructure.Persistence;
using Souq.Infrastructure.Persistence.Interceptors;
using Souq.IntegrationTests.Infrastructure;

namespace Souq.IntegrationTests;

// الختم الزمني للإنشاء/التعديل يأتي من TimeProvider عبر المعترِض (Phase 0 D12) — نثبت
// ذلك على SQL Server الحقيقي بساعة ثابتة بعيدة عن "الآن".
[Collection(IntegrationCollection.Name)]
public class AuditTimestampsTests
{
    private readonly SouqApiFactory _factory;

    public AuditTimestampsTests(SouqApiFactory factory) => _factory = factory;

    [Fact]
    public async Task الإنشاء_والتعديل_يُختمان_من_الساعة_المحقونة()
    {
        _factory.CreateClient(); // يُقلع الخادم ⇒ الهجرات مطبّقة
        var tenant = SouqApiFactory.ContextFor(await _factory.DefaultTenantAsync());
        var created = new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var clock = new SettableClock(created);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(_factory.ConnectionString)
            .AddInterceptors(
                new AuditTimestampsInterceptor(clock),
                new TenantWriteGuardInterceptor(NullLogger<TenantWriteGuardInterceptor>.Instance))
            .Options;
        var slug = $"audit-{Guid.NewGuid():N}"[..24];

        int id;
        await using (var db = new AppDbContext(options, tenant))
        {
            var category = new Category("تدقيق", slug);
            db.Categories.Add(category);
            await db.SaveChangesAsync();
            id = category.Id;
        }

        clock.Now = created.AddHours(5);
        await using (var db = new AppDbContext(options, tenant))
        {
            var category = await db.Categories.SingleAsync(c => c.Id == id);
            category.UpdateDetails("تدقيق معدّل", slug);
            await db.SaveChangesAsync();
        }

        await using (var db = new AppDbContext(options, tenant))
        {
            var stored = await db.Categories.AsNoTracking().SingleAsync(c => c.Id == id);
            stored.CreatedAt.Should().Be(created.UtcDateTime);
            stored.UpdatedAt.Should().Be(created.AddHours(5).UtcDateTime);
        }
    }

    private sealed class SettableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
