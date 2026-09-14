using System.Data;
using AwesomeAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Souq.Application.Common.Tenancy;
using Souq.Infrastructure.Persistence;
using Souq.IntegrationTests.Infrastructure;

namespace Souq.IntegrationTests;

// ============================================================================
// M9 — الرجوع بالمخطّط يفقد بيانات، بالدليل لا بالتحذير.
//
// كل هجرة هنا لها Down()، وخمس منها تحاول إعادة ملء ما نقلته. وجود Down يوحي بأن الرجوع
// عملية آمنة، وهو ما يدفع مشغّلاً تحت ضغط عُطل إلى تجربتها على الإنتاج. هذا الاختبار يُظهر
// الفقد على قاعدة تُحذف بعده: منتج بثلاث لغات، رجوع خطوة واحدة، ثم تقدّم — تعود لغتان.
//
// الخلاصة التشغيلية ليست "أصلحوا Down": المخطّط القديم لا مكان فيه للغة ثالثة أصلاً. الخلاصة
// أن الرجوع الآمن هو استعادة نسخة احتياطية (BackupAndRestore.md §6)، لا `database update`.
// ============================================================================
[Collection(IntegrationCollection.Name)]
public class MigrationRollbackTests
{
    private const string BeforeCatalogPhase = "20260911122627_Phase4PlatformAdministration";

    private readonly SouqApiFactory _factory;

    public MigrationRollbackTests(SouqApiFactory factory) => _factory = factory;

    [Fact]
    public async Task الرجوع_عبر_هجرة_ناقلة_للبيانات_يفقد_ما_لا_مكان_له_في_المخطّط_القديم()
    {
        _factory.CreateClient();
        var connectionString = new SqlConnectionStringBuilder(_factory.ConnectionString)
        {
            InitialCatalog = $"rollback_{Guid.NewGuid():N}"[..28],
        }.ConnectionString;
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options;

        await using var db = new AppDbContext(options, new TenantContext());
        try
        {
            await db.GetService<IMigrator>().MigrateAsync();          // إلى الأحدث
            await ExecuteAsync(db, SeedProductWithThreeCultures);

            (await ScalarAsync(db, "SELECT COUNT(*) FROM [ProductTranslations]")).Should().Be(3);

            // خطوة واحدة إلى الوراء، ثم إلى الأمام — أبسط سيناريو "تراجعنا عن النشر".
            await db.GetService<IMigrator>().MigrateAsync(BeforeCatalogPhase);
            await db.GetService<IMigrator>().MigrateAsync();

            var cultures = (string?)await ScalarAsync(db,
                "SELECT STRING_AGG([Culture], ',') WITHIN GROUP (ORDER BY [Culture]) FROM [ProductTranslations]");

            cultures.Should().Be("ar,en", "المخطّط القديم يحمل NameAr و NameEn فقط — الثالثة لا مكان لها");
            (await ScalarAsync(db, "SELECT COUNT(*) FROM [ProductTranslations] WHERE [Culture] = N'fr'"))
                .Should().Be(0, "وفقدها صامت: لا خطأ ولا تحذير من قاعدة البيانات");

            // والصورة الثانية أيضاً: المخطّط القديم يحمل ImageUrl واحداً.
            (await ScalarAsync(db, "SELECT COUNT(*) FROM [ProductImages]"))
                .Should().Be(1, "الصور بعد الأولى لا مكان لها في العمود المفرد القديم");

            // وما نجا نجا سليماً: الرجوع ليس تلفاً عشوائياً، بل فقدٌ لما لا يُمثَّل.
            (await ScalarAsync(db, "SELECT [Name] FROM [ProductTranslations] WHERE [Culture] = N'ar'"))
                .Should().Be("قميص");
            (await ScalarAsync(db, "SELECT [Url] FROM [ProductImages]")).Should().Be("/uploads/first.png");
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    // منتج واحد بثلاث لغات على المخطّط الأحدث. المتجر رقم 1 تكتبه هجرة المرحلة 2.
    private const string SeedProductWithThreeCultures = """
        DECLARE @now datetime2 = SYSUTCDATETIME();
        INSERT INTO [Categories] ([TenantId], [Slug], [IsActive], [SortOrder], [CreatedAt]) VALUES (1, N'shirts', 1, 0, @now);
        DECLARE @category int = SCOPE_IDENTITY();
        INSERT INTO [Products] ([TenantId], [CategoryId], [Slug], [Status], [CreatedAt])
            VALUES (1, @category, N'shirt', 1, @now);
        DECLARE @product int = SCOPE_IDENTITY();
        INSERT INTO [ProductTranslations] ([TenantId], [ProductId], [Culture], [Name], [CreatedAt]) VALUES
            (1, @product, N'ar', N'قميص',  @now),
            (1, @product, N'en', N'Shirt', @now),
            (1, @product, N'fr', N'Chemise', @now);
        -- المتغيّر الافتراضي وصورتان: منتج بشكله الحقيقي. Down يستعيد عبر JOIN على المتغيّر
        -- الافتراضي، فمنتج بلا متغيّر يخرج من الاستعادة كلياً — والاختبار يجب أن يقيس الحالة
        -- الواقعية لا حالة بيانات ناقصة.
        INSERT INTO [ProductVariants] ([TenantId], [ProductId], [Price], [Currency], [IsDefault], [CreatedAt])
            VALUES (1, @product, 10.0000, N'JOD', 1, @now);
        INSERT INTO [ProductImages] ([TenantId], [ProductId], [Url], [SortOrder], [CreatedAt]) VALUES
            (1, @product, N'/uploads/first.png',  0, @now),
            (1, @product, N'/uploads/second.png', 1, @now);
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
