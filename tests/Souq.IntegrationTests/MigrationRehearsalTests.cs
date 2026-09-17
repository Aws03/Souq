using System.Data;
using AwesomeAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Souq.Application.Common.Tenancy;
using Souq.Domain.Platform;
using Souq.Infrastructure.Persistence;
using Souq.Infrastructure.Services;
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
    private const string BeforeVariantMigration = "20260911200431_Phase14Notifications";
    private const string LegacyPassword = "Legacy-Pass-1";

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
            await ExecuteAsync(db, LegacyRows.Replace("{legacy-hash}", new BcryptPasswordHasher().Hash(LegacyPassword)));
            var before = new Dictionary<string, int>();
            foreach (var table in TenantOwnedTables)
                before[table] = Convert.ToInt32(await ScalarAsync(db, $"SELECT COUNT(*) FROM [{table}]"));

            await db.GetService<IMigrator>().MigrateAsync();

            (await ScalarAsync(db, "SELECT COUNT(*) FROM [Tenants] WHERE [Id] = 1 AND [Slug] = N'marka' AND [Status] = 1"))
                .Should().Be(1);
            foreach (var table in TenantOwnedTables)
            {
                // المرحلة 6 تضيف سطر رصيد افتتاحي واحداً للمنتج القديم (سجلّه لم يطابق مخزونه) — ولا تحذف شيئاً.
                var expected = table == "StockMovements" ? before[table] + 1 : before[table];
                (await ScalarAsync(db, $"SELECT COUNT(*) FROM [{table}]")).Should().Be(expected, $"{table}: لا صف مفقود");
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

            // المرحلة 3: كل عميل صار حساب دخول بالمعرّف والمتجر والبريد نفسه، والكلمة القديمة ما زالت تعمل،
            // والدور القديم Admin صار TenantAdmin، ولا اعتماد متبقٍّ في Customers.
            (await ScalarAsync(db, "SELECT COUNT(*) FROM [Users]")).Should().Be(before["Customers"]);
            (await ScalarAsync(db, """
                SELECT COUNT(*) FROM [Customers] c JOIN [Users] u
                  ON u.[Id] = c.[UserId] AND u.[Id] = c.[Id] AND u.[TenantId] = c.[TenantId] AND u.[Email] = c.[Email]
                """)).Should().Be(before["Customers"]);
            (await ScalarAsync(db, "SELECT [Role] FROM [Users] WHERE [Email] = N'legacy-admin@souq.test'")).Should().Be("TenantAdmin");
            (await ScalarAsync(db, "SELECT [Role] FROM [Users] WHERE [Email] = N'legacy@souq.test'")).Should().Be("Customer");
            var migratedHash = (string)(await ScalarAsync(db, "SELECT [PasswordHash] FROM [Users] WHERE [Email] = N'legacy@souq.test'"))!;
            new BcryptPasswordHasher().Verify(LegacyPassword, migratedHash).Should().BeTrue();
            (await ScalarAsync(db, """
                SELECT COUNT(*) FROM sys.columns WHERE [object_id] = OBJECT_ID(N'[Customers]') AND [name] IN (N'PasswordHash', N'Role')
                """)).Should().Be(0);

            // المرحلة 5: النصوص والسعر والحالة نُقلت قبل حذف أعمدتها؛ مفتاح الصورة القديم لم يكن صورة فلا يُنقل.
            (await ScalarAsync(db, """
                SELECT COUNT(*) FROM [ProductTranslations]
                WHERE [Culture] = N'ar' AND [Name] = N'منتج قديم' AND [Description] = N'وصف'
                """)).Should().Be(1);
            (await ScalarAsync(db, "SELECT COUNT(*) FROM [ProductTranslations] WHERE [Culture] = N'en' AND [Name] = N'Legacy product'"))
                .Should().Be(1);
            (await ScalarAsync(db, "SELECT COUNT(*) FROM [ProductVariants] WHERE [IsDefault] = 1")).Should().Be(before["Products"]);
            (await ScalarAsync(db, """
                SELECT v.[Price] FROM [ProductVariants] v JOIN [ProductTranslations] t ON t.[ProductId] = v.[ProductId]
                WHERE t.[Name] = N'منتج قديم' AND v.[IsDefault] = 1 AND v.[Currency] = N'KWD'
                """)).Should().Be(12.5m);
            (await ScalarAsync(db, "SELECT COUNT(*) FROM [ProductImages]")).Should().Be(0);
            (await ScalarAsync(db, "SELECT COUNT(*) FROM [Products] WHERE [Status] = 1 AND [Slug] = CONCAT(N'p-', [Id])"))
                .Should().Be(before["Products"]);
            (await ScalarAsync(db, "SELECT [Name] FROM [CategoryTranslations] WHERE [Culture] = N'ar'")).Should().Be("فئة قديمة");
            (await ScalarAsync(db, """
                SELECT COUNT(*) FROM sys.columns
                WHERE ([object_id] = OBJECT_ID(N'[Products]') AND [name] IN (N'NameAr', N'NameEn', N'Description', N'Price', N'Currency', N'ImageUrl', N'IsActive'))
                   OR ([object_id] = OBJECT_ID(N'[Categories]') AND [name] = N'Name')
                """)).Should().Be(0);
            foreach (var table in new[] { "ProductTranslations", "ProductVariants", "CategoryTranslations", "InventoryItems", "StockReservations", "CouponRedemptions", "Payments" })
                (await ScalarAsync(db, $"SELECT COUNT(*) FROM [{table}] WHERE [TenantId] <> 1")).Should().Be(0, table);

            // المرحلة 6: الطلب المعلّق كان قد أنقص المخزون (5 متبقية) ⇒ الموجود 5 + 2 محجوزة له بحجز نشط؛ المدفوع غير
            // المشحون حجز ملتزم (إلغاؤه يعيد مخزونه)؛ ورصيد افتتاحي يجعل Σ السجلّ = الموجود؛ وأعمدة Products القديمة حُذفت.
            (await ScalarAsync(db, "SELECT COUNT(*) FROM [InventoryItems]")).Should().Be(before["Products"]);
            (await ScalarAsync(db, "SELECT CONCAT([OnHand], N'/', [Reserved], N'/', [LowStockThreshold]) FROM [InventoryItems]"))
                .Should().Be("7/2/5");
            (await ScalarAsync(db, """
                SELECT STRING_AGG(CONCAT(o.[ShippingAddress], N':', r.[Quantity], N':', r.[Status]), N',') WITHIN GROUP (ORDER BY r.[Status])
                FROM [StockReservations] r JOIN [Orders] o ON r.[Reference] = CONCAT(N'order:', o.[Id])
                """)).Should().Be("معلّق:2:0,مدفوع:1:1");
            (await ScalarAsync(db, "SELECT SUM([QuantityChange]) FROM [StockMovements]")).Should().Be(7);
            (await ScalarAsync(db, "SELECT COUNT(*) FROM [StockMovements] WHERE [InventoryItemId] IS NULL")).Should().Be(0);
            (await ScalarAsync(db, """
                SELECT COUNT(*) FROM sys.columns
                WHERE [object_id] = OBJECT_ID(N'[Products]') AND [name] IN (N'StockQuantity', N'LowStockThreshold')
                """)).Should().Be(0);

            // المرحلة 9: كل طلب قديم أخذ رقماً من 1001 بترتيب إنشائه ورمز تتبّع فريداً، والفوترة = الشحن، والتثبيت بإجماليات
            // أسطره وخصمه؛ وعدّاد المتجر عند آخر رقم — فالطلب التالي لا يصطدم بالقديم.
            (await ScalarAsync(db, "SELECT CONCAT(MIN([OrderNumber]), N'-', MAX([OrderNumber]), N'-', COUNT(DISTINCT [OrderNumber])) FROM [Orders]"))
                .Should().Be($"1001-{1000 + before["Orders"]}-{before["Orders"]}");
            (await ScalarAsync(db, """
                SELECT COUNT(DISTINCT [TrackingToken]) FROM [Orders]
                WHERE LEN([TrackingToken]) = 32 AND [TrackingToken] NOT LIKE N'%[^0-9a-f]%'
                """)).Should().Be(before["Orders"]);
            (await ScalarAsync(db, """
                SELECT COUNT(*) FROM [Orders] o
                WHERE o.[BillingAddress] = o.[ShippingAddress] AND o.[PlacedAt] = o.[CreatedAt]
                  AND o.[PlacedSubtotal] = ISNULL((SELECT SUM(i.[UnitPrice] * i.[Quantity]) FROM [OrderItems] i WHERE i.[OrderId] = o.[Id]), 0)
                  AND o.[PlacedTotal] = o.[PlacedSubtotal] - ISNULL(o.[DiscountAmount], 0)
                """)).Should().Be(before["Orders"]);
            (await ScalarAsync(db, "SELECT [LastNumber] FROM [OrderNumberSequences] WHERE [TenantId] = 1")).Should().Be(1000 + before["Orders"]);

            // المرحلة 10: كل طلب قائم بكوبون أخذ سجلّ استخدامه بخصمه — المعلّق محجوز والمدفوع مؤكَّد والملغى بلا سجلّ؛ والعدّاد
            // القديم (احتسب المدفوع عند دفعه) زاد بالمعلّق وحده، فدفعه لاحقاً لا يُعدّ مرتين وإلغاؤه يعيده.
            (await ScalarAsync(db, """
                SELECT STRING_AGG(CONCAT(o.[ShippingAddress], N':', r.[Status], N':', r.[DiscountAmount], N':', r.[Currency]), N',')
                       WITHIN GROUP (ORDER BY r.[Status])
                FROM [CouponRedemptions] r
                JOIN [Orders] o ON o.[TenantId] = r.[TenantId] AND o.[Id] = r.[OrderId] AND o.[CustomerId] = r.[CustomerId]
                JOIN [Coupons] c ON c.[TenantId] = r.[TenantId] AND c.[Id] = r.[CouponId] AND c.[Code] = N'LEGACY10'
                """)).Should().Be("معلّق:0:2.5000:KWD,مدفوع:1:1.2500:KWD");
            (await ScalarAsync(db, "SELECT [UsedCount] FROM [Coupons] WHERE [Code] = N'LEGACY10'")).Should().Be(2);

            // المرحلة 11: كل طلب له نيّة دفع أخذ دفعته بمبلغه المثبَّت وعملته والحساب الذي أنشأها — المعلّق معلّقة، والمسلَّم
            // والمدفوع ناجحتان، والملغى الذي شهد سجلّه دفعه قبل إلغائه ناجحة (فتستردّها الإدارة الآن)؛ ولا استرداد قبل المرحلة.
            (await ScalarAsync(db, """
                SELECT STRING_AGG(CONCAT(o.[ShippingAddress], N':', p.[Gateway], N':', p.[Status], N':', p.[Amount], N':', p.[Currency]), N',')
                       WITHIN GROUP (ORDER BY o.[Id])
                FROM [Payments] p
                JOIN [Orders] o ON o.[TenantId] = p.[TenantId] AND o.[Id] = p.[OrderId] AND o.[PaymentIntentId] = p.[ProviderPaymentId]
                """)).Should().Be(
                "بأسطر:stripe:deployment:1:25.0000:KWD,بلا أسطر:stripe:deployment:1:0.0000:JOD," +
                "معلّق:fake:0:22.5000:KWD,مدفوع:stripe:deployment:1:11.2500:KWD");
            (await ScalarAsync(db, "SELECT COUNT(*) FROM [Refunds]")).Should().Be(0);

            // المرحلة 12: الطلبات القديمة بلا شحن — تكلفة صفر وبلا طريقة، فإجمالياتها المثبَّتة كما هي (الفحص أعلاه).
            (await ScalarAsync(db, "SELECT COUNT(*) FROM [Orders] WHERE [ShippingAmount] <> 0 OR [ShippingMethodName] IS NOT NULL"))
                .Should().Be(0);

            // المتغيّرات V1 (OrderLinesRecordVariant): كل سطر طلب قديم رُبط بالمتغيّر الافتراضي لمنتجه في متجره — ربطاً يقينياً
            // لا تخميناً — ولقطتا SKU والوصف فارغتان (لم تُسجَّلا لحظة البيع)، وكل متغيّر قائم نشط، ولا قيمة افتراضية متبقية.
            (await ScalarAsync(db, """
                SELECT COUNT(*) FROM [OrderItems] i
                JOIN [ProductVariants] v ON v.[TenantId] = i.[TenantId] AND v.[Id] = i.[VariantId] AND v.[ProductId] = i.[ProductId] AND v.[IsDefault] = 1
                """)).Should().Be(before["OrderItems"]);
            (await ScalarAsync(db, "SELECT COUNT(*) FROM [OrderItems] WHERE [Sku] IS NOT NULL OR [VariantLabel] IS NOT NULL")).Should().Be(0);
            (await ScalarAsync(db, "SELECT COUNT(*) FROM [ProductVariants] WHERE [IsActive] = 0")).Should().Be(0);
            (await ScalarAsync(db, """
                SELECT COUNT(*) FROM sys.default_constraints d
                JOIN sys.columns c ON c.[object_id] = d.[parent_object_id] AND c.[column_id] = d.[parent_column_id]
                WHERE d.[parent_object_id] = OBJECT_ID(N'[OrderItems]') AND c.[name] = N'VariantId'
                """)).Should().Be(0);

            // المرشّحات على البيانات المُرحَّلة: المتجر 1 يرى صفوفه، ومتجر آخر لا يرى شيئاً — والتجمّع يُقرأ كاملاً.
            await using (var asDefault = new AppDbContext(options, Context(1)))
            {
                (await asDefault.Customers.CountAsync(c => c.Email == "legacy@souq.test")).Should().Be(1);
                (await asDefault.Users.CountAsync(u => u.NormalizedEmail == "LEGACY@SOUQ.TEST")).Should().Be(1);
                var legacy = await asDefault.Products.Include(p => p.Translations).Include(p => p.Variants)
                    .SingleAsync(p => p.Translations.Any(t => t.Name == "منتج قديم"));
                legacy.NameIn("en").Should().Be("Legacy product");
                legacy.Price.Should().Be(new Souq.Domain.ValueObjects.Money(12.5m, "KWD"));
                legacy.IsActive.Should().BeTrue();
                (await asDefault.InventoryItems.SingleAsync(i => i.ProductId == legacy.Id)).Available.Should().Be(5);
            }
            await using (var asOther = new AppDbContext(options, Context(999)))
                (await asOther.Customers.CountAsync()).Should().Be(0);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task ربط_أسطر_الطلبات_بمتغيّراتها_يتوقّف_بدل_التخمين_حين_لمنتج_متغيّر_ثانٍ()
    {
        // الربط يقيني فقط لأن لكل منتج متغيّراً واحداً طوال عمره. قاعدة فيها متغيّر غير افتراضي (غير ممكن من التطبيق قبل
        // الهجرة) لا تُرحَّل بتخمين: الهجرة تتوقّف ومعاملتها تُلغى كلها، والمخطّط يبقى كما كان.
        _factory.CreateClient();
        var connectionString = new SqlConnectionStringBuilder(_factory.ConnectionString)
        {
            InitialCatalog = $"variantguard_{Guid.NewGuid():N}"[..28],
        }.ConnectionString;
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options;

        await using var db = new AppDbContext(options, new TenantContext());
        try
        {
            await db.GetService<IMigrator>().MigrateAsync(BeforeVariantMigration);
            await ExecuteAsync(db, """
                DECLARE @now datetime2 = SYSUTCDATETIME();
                INSERT INTO [Categories] ([TenantId], [Slug], [IsActive], [SortOrder], [CreatedAt]) VALUES (1, N'guard', 1, 0, @now);
                DECLARE @category int = SCOPE_IDENTITY();
                INSERT INTO [Products] ([TenantId], [Slug], [Status], [CategoryId], [CreatedAt]) VALUES (1, N'guard', 1, @category, @now);
                DECLARE @product int = SCOPE_IDENTITY();
                INSERT INTO [ProductVariants] ([TenantId], [ProductId], [IsDefault], [Price], [Currency], [CreatedAt])
                    VALUES (1, @product, 1, 10, N'JOD', @now), (1, @product, 0, 12, N'JOD', @now);
                """);

            var migrate = () => db.GetService<IMigrator>().MigrateAsync();

            (await migrate.Should().ThrowAsync<SqlException>()).Which.Message.Should().Contain("OrderLinesRecordVariant");
            (await ScalarAsync(db, "SELECT COUNT(*) FROM [__EFMigrationsHistory] WHERE [MigrationId] LIKE N'%OrderLinesRecordVariant'"))
                .Should().Be(0);
            (await ScalarAsync(db, "SELECT COUNT(*) FROM sys.columns WHERE [object_id] = OBJECT_ID(N'[OrderItems]') AND [name] = N'VariantId'"))
                .Should().Be(0);
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
            VALUES (N'عميل قديم', N'legacy@souq.test', N'{legacy-hash}', N'Customer', @now);
        DECLARE @customer int = SCOPE_IDENTITY();
        INSERT INTO [Customers] ([FullName], [Email], [PasswordHash], [Role], [CreatedAt])
            VALUES (N'مدير قديم', N'legacy-admin@souq.test', N'{legacy-hash}', N'Admin', @now);
        INSERT INTO [Products] ([NameAr], [NameEn], [Description], [Price], [Currency], [StockQuantity], [LowStockThreshold],
                                [ImageUrl], [IsActive], [CategoryId], [CreatedAt])
            VALUES (N'منتج قديم', N'Legacy product', N'وصف', 12.500, N'KWD', 5, 5, N'legacy', 1, @category, @now);
        DECLARE @product int = SCOPE_IDENTITY();
        INSERT INTO [Orders] ([CustomerId], [Status], [ShippingAddress], [PaymentIntentId], [CreatedAt])
            VALUES (@customer, 3, N'بأسطر', N'pi_legacy_delivered', @now);
        DECLARE @order int = SCOPE_IDENTITY();
        INSERT INTO [OrderItems] ([OrderId], [ProductId], [ProductName], [UnitPrice], [Currency], [Quantity], [CreatedAt])
            VALUES (@order, @product, N'منتج قديم', 12.500, N'KWD', 2, @now);
        INSERT INTO [OrderStatusHistories] ([OrderId], [Status], [CreatedAt]) VALUES (@order, 0, @now);
        INSERT INTO [Orders] ([CustomerId], [Status], [ShippingAddress], [CouponCode], [PaymentIntentId], [CreatedAt])
            VALUES (@customer, 4, N'بلا أسطر', N'LEGACY10', N'pi_legacy_paid_then_cancelled', @now);
        INSERT INTO [OrderStatusHistories] ([OrderId], [Status], [CreatedAt]) VALUES (SCOPE_IDENTITY(), 1, @now);
        INSERT INTO [Orders] ([CustomerId], [Status], [ShippingAddress], [CouponCode], [DiscountAmount], [DiscountCurrency],
                              [PaymentIntentId], [CreatedAt])
            VALUES (@customer, 0, N'معلّق', N'LEGACY10', 2.500, N'KWD', N'pi_fake_legacy', @now);
        INSERT INTO [OrderItems] ([OrderId], [ProductId], [ProductName], [UnitPrice], [Currency], [Quantity], [CreatedAt])
            VALUES (SCOPE_IDENTITY(), @product, N'منتج قديم', 12.500, N'KWD', 2, @now);
        INSERT INTO [Orders] ([CustomerId], [Status], [ShippingAddress], [CouponCode], [DiscountAmount], [DiscountCurrency],
                              [PaymentIntentId], [CreatedAt])
            VALUES (@customer, 1, N'مدفوع', N'LEGACY10', 1.250, N'KWD', N'pi_legacy_paid', @now);
        INSERT INTO [OrderItems] ([OrderId], [ProductId], [ProductName], [UnitPrice], [Currency], [Quantity], [CreatedAt])
            VALUES (SCOPE_IDENTITY(), @product, N'منتج قديم', 12.500, N'KWD', 1, @now);
        INSERT INTO [Coupons] ([Code], [Type], [Value], [UsedCount], [IsActive], [CreatedAt])
            VALUES (N'LEGACY10', 0, 10, 1, 1, @now);
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
