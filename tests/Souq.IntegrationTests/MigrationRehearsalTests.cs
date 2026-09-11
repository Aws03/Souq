using System.Data;
using AwesomeAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Souq.Application.Common.Tenancy;
using Souq.Domain.Platform;
using Souq.Infrastructure.Persistence;
using Souq.IntegrationTests.Infrastructure;

namespace Souq.IntegrationTests;

// ============================================================================
// تجربة هجرة المرحلة 2 على بيانات حقيقية بشكل ما قبلها (خطر R4 في خارطة الطريق): قاعدة منفصلة على
// الخادم نفسه تُرحَّل حتى نهاية المرحلة 1A، تُملأ بصفوف بمخطّطها القديم (SQL خام كما كانت)، ثم
// تُرحَّل حتى الأحدث. نثبت: لا صف مفقود، كل صف للمتجر الافتراضي، عملة الطلب من أسطره، لا قيد
// افتراضي متبقٍّ على TenantId، والمرشّحات تعمل على البيانات المُرحَّلة.
// ============================================================================
[Collection(IntegrationCollection.Name)]
public class MigrationRehearsalTests
{
    private const string LastPhase1Migration = "20260911061506_Phase1AIntegrityPrecisionConcurrency";

    private static readonly string[] TenantOwnedTables =
    [
        "Categories", "Customers", "Products", "Orders", "OrderItems",
        "OrderStatusHistories", "Coupons", "StockMovements", "Reviews",
    ];

    private readonly SouqApiFactory _factory;

    public MigrationRehearsalTests(SouqApiFactory factory) => _factory = factory;

    [Fact]
    public async Task بيانات_ما_قبل_المرحلة_2_تنتقل_للمتجر_الافتراضي_بلا_فقد()
    {
        _factory.CreateClient();
        var connectionString = new SqlConnectionStringBuilder(_factory.ConnectionString)
        {
            InitialCatalog = $"rehearsal_{Guid.NewGuid():N}",
        }.ConnectionString;
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options;

        await using var db = new AppDbContext(options, new TenantContext());
        try
        {
            await db.GetService<IMigrator>().MigrateAsync(LastPhase1Migration);
            await ExecuteAsync(db, LegacyRows);
            var before = new Dictionary<string, int>();
            foreach (var table in TenantOwnedTables)
                before[table] = Convert.ToInt32(await ScalarAsync(db, $"SELECT COUNT(*) FROM [{table}]"));

            await db.GetService<IMigrator>().MigrateAsync();

            (await ScalarAsync(db, "SELECT COUNT(*) FROM [Tenants] WHERE [Id] = 1 AND [Slug] = N'marka' AND [Status] = 1"))
                .Should().Be(1);
            foreach (var table in TenantOwnedTables)
            {
                (await ScalarAsync(db, $"SELECT COUNT(*) FROM [{table}]")).Should().Be(before[table], $"{table}: لا صف مفقود");
                (await ScalarAsync(db, $"SELECT COUNT(*) FROM [{table}] WHERE [TenantId] <> 1")).Should().Be(0, table);
            }

            (await ScalarAsync(db, "SELECT [Currency] FROM [Orders] WHERE [ShippingAddress] = N'بأسطر'")).Should().Be("KWD");
            (await ScalarAsync(db, "SELECT [Currency] FROM [Orders] WHERE [ShippingAddress] = N'بلا أسطر'")).Should().Be("JOD");

            // لا قيد افتراضي متبقٍّ: صف جديد بلا متجر صريح يُرفض بدل أن يذهب بصمت للمتجر 1.
            (await ScalarAsync(db, """
                SELECT COUNT(*) FROM sys.default_constraints d
                JOIN sys.columns c ON c.[object_id] = d.[parent_object_id] AND c.[column_id] = d.[parent_column_id]
                WHERE c.[name] = N'TenantId' OR (c.[name] = N'Currency' AND OBJECT_NAME(d.[parent_object_id]) = N'Orders')
                """)).Should().Be(0);
            var insertWithoutTenant = () => ExecuteAsync(db,
                "INSERT INTO [Categories] ([Name], [Slug], [CreatedAt]) VALUES (N'بلا متجر', N'no-tenant', SYSUTCDATETIME())");
            await insertWithoutTenant.Should().ThrowAsync<SqlException>();

            // المرشّحات على البيانات المُرحَّلة: المتجر 1 يرى صفوفه، ومتجر آخر لا يرى شيئاً.
            await using (var asDefault = new AppDbContext(options, Context(1)))
                (await asDefault.Customers.CountAsync(c => c.Email == "legacy@souq.test")).Should().Be(1);
            await using (var asOther = new AppDbContext(options, Context(999)))
                (await asOther.Customers.CountAsync()).Should().Be(0);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    private static TenantContext Context(int tenantId) => SouqApiFactory.ContextFor(
        new TenantInfo(tenantId, "marka", "Marka Demo", TenantStatus.Active, "JOD", "ar", "Asia/Amman"));

    // صفوف بمخطّط المرحلة 1 كما كانت (قبل TenantId وعملة الطلب).
    private const string LegacyRows = """
        DECLARE @now datetime2 = SYSUTCDATETIME();
        INSERT INTO [Categories] ([Name], [Slug], [CreatedAt]) VALUES (N'فئة قديمة', N'legacy-cat', @now);
        DECLARE @category int = SCOPE_IDENTITY();
        INSERT INTO [Customers] ([FullName], [Email], [PasswordHash], [Role], [CreatedAt])
            VALUES (N'عميل قديم', N'legacy@souq.test', N'$2a$11$legacy', N'Customer', @now);
        DECLARE @customer int = SCOPE_IDENTITY();
        INSERT INTO [Products] ([NameAr], [NameEn], [Description], [Price], [Currency], [StockQuantity], [LowStockThreshold],
                                [ImageUrl], [IsActive], [CategoryId], [CreatedAt])
            VALUES (N'منتج قديم', N'Legacy product', N'وصف', 12.500, N'KWD', 5, 5, N'legacy', 1, @category, @now);
        DECLARE @product int = SCOPE_IDENTITY();
        INSERT INTO [Orders] ([CustomerId], [Status], [ShippingAddress], [CreatedAt]) VALUES (@customer, 3, N'بأسطر', @now);
        DECLARE @order int = SCOPE_IDENTITY();
        INSERT INTO [OrderItems] ([OrderId], [ProductId], [ProductName], [UnitPrice], [Currency], [Quantity], [CreatedAt])
            VALUES (@order, @product, N'منتج قديم', 12.500, N'KWD', 2, @now);
        INSERT INTO [OrderStatusHistories] ([OrderId], [Status], [CreatedAt]) VALUES (@order, 0, @now);
        INSERT INTO [Orders] ([CustomerId], [Status], [ShippingAddress], [CreatedAt]) VALUES (@customer, 4, N'بلا أسطر', @now);
        INSERT INTO [Coupons] ([Code], [Type], [Value], [UsedCount], [IsActive], [CreatedAt])
            VALUES (N'LEGACY10', 0, 10, 0, 1, @now);
        INSERT INTO [StockMovements] ([ProductId], [Type], [QuantityChange], [NewQuantity], [CreatedAt])
            VALUES (@product, 0, 5, 5, @now);
        INSERT INTO [Reviews] ([ProductId], [CustomerId], [OrderId], [Rating], [Comment], [CreatedAt])
            VALUES (@product, @customer, @order, 5, N'ممتاز', @now);
        """;

    private static async Task ExecuteAsync(AppDbContext db, string sql)
    {
        await using var command = await CommandAsync(db, sql);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(AppDbContext db, string sql)
    {
        await using var command = await CommandAsync(db, sql);
        return await command.ExecuteScalarAsync();
    }

    private static async Task<System.Data.Common.DbCommand> CommandAsync(AppDbContext db, string sql)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = sql;
        return command;
    }
}
