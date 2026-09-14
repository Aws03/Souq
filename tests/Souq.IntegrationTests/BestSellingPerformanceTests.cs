using System.Diagnostics;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Souq.Application.Common.Tenancy;
using Souq.Infrastructure.Persistence;
using Souq.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace Souq.IntegrationTests;

// ============================================================================
// F-17 — قياس، لا تخمين.
//
// صفحة المتجر الرئيسية تطلب sortBy=BestSelling لكل زائر مجهول (Store.jsx)، و"الأكثر مبيعاً"
// استعلام فرعي مترابط يجمع كميات كل الطلبات المُسلَّمة لكل منتج في الصفحة. السؤال ليس "هل
// يبدو مكلفاً" بل "كم يكلّف فعلاً، وعند أي حجم يصير مشكلة" — قبل أي حديث عن ذاكرة مؤقتة.
//
// يُقاس على قاعدة تُحذف بعده، ببيانات مصنوعة بأحجام متدرّجة، مقارنةً بالفرز الافتراضي.
// ============================================================================
[Collection(IntegrationCollection.Name)]
public class BestSellingPerformanceTests
{
    private readonly SouqApiFactory _shared;
    private readonly ITestOutputHelper _output;

    public BestSellingPerformanceTests(SouqApiFactory shared, ITestOutputHelper output)
    {
        _shared = shared; _output = output;
    }

    // حجم متواضع يبقى في كل تشغيل: الغرض تثبيت *شكل* الاستعلام لا قياس الأرقام المطلقة
    // (القياسات الكبيرة موثّقة في ScalingStrategy.md — وهي بطيئة ومرهونة بعتاد الجهاز).
    [Theory]
    [InlineData(60, 600, false)]
    public async Task الأكثر_مبيعاً_استعلام_واحد_لا_استعلام_لكل_منتج(int products, int orders, bool coveringIndex)
    {
        await using var app = new ScratchApp(_shared.ConnectionString);
        // عميل حقيقي عبر الـ API: الطلب يحتاج Customer الذي يحتاج User، وبناء تلك السلسلة
        // بـ SQL يدوي يعيد إنتاج قواعد لا تخصّ القياس. تسجيل واحد يكفي لكل الطلبات المصنوعة.
        var registration = new StringContent("{\"fullName\":\"perf\",\"email\":\"perf@souq.test\",\"password\":\"Perf-Test-2026\"}",
            System.Text.Encoding.UTF8, "application/json");
        (await app.Client().PostAsync("/api/auth/register", registration)).EnsureSuccessStatusCode();
        await SeedAsync(app, products, orders);
        if (coveringIndex) await AddCoveringIndexAsync(app);

        var client = app.Client();
        await client.GetAsync("/api/products?pageSize=12");     // إحماء: خطط التنفيذ والاتصالات

        var newest = await TimeAsync(client, "/api/products?pageSize=12&sortBy=Newest");
        var best = await TimeAsync(client, "/api/products?pageSize=12&sortBy=BestSelling");

        _output.WriteLine($"products={products} orders={orders} orderItems≈{orders * 2} coveringIndex={coveringIndex}");
        _output.WriteLine($"  Newest      median {newest.Median:0.0} ms   max {newest.Max:0.0} ms");
        _output.WriteLine($"  BestSelling median {best.Median:0.0} ms   max {best.Max:0.0} ms");
        _output.WriteLine($"  ratio       {best.Median / Math.Max(newest.Median, 0.01):0.0}×");
        _output.WriteLine($"  SQL: {app.LastSelect()}");

        // الحارس الحقيقي: الفرز يبقى داخل SQL في جملة واحدة. انهياره إلى استعلام لكل منتج
        // (N+1) هو الانحدار الذي يقتل الصفحة فعلاً، وهو ما قد يُدخِله تعديل بريء على الفرز.
        app.SelectCount().Should().BeLessThan(products,
            "صفحة واحدة من الأكثر مبيعاً لا تُصدر استعلاماً لكل منتج");
        best.Median.Should().BeGreaterThan(0);
    }

    // الفهرس المرشَّح: المفتاح موجود أصلاً (TenantId, ProductId) لكن بلا Quantity ولا OrderId،
    // فكل صف مطابق يستدعي بحثاً إضافياً في الجدول. التغطية تحذف تلك المراجعات.
    private static async Task AddCoveringIndexAsync(ScratchApp app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().UseTenant(await app.DefaultTenantAsync());
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();
        await ExecuteAsync(connection, """
            CREATE INDEX [IX_OrderItems_TenantId_ProductId_Covering]
                ON [OrderItems] ([TenantId], [ProductId]) INCLUDE ([OrderId], [Quantity]);
            """);
    }

    private static async Task<(double Median, double Max)> TimeAsync(HttpClient client, string url)
    {
        var samples = new List<double>();
        for (var i = 0; i < 7; i++)
        {
            var started = Stopwatch.GetTimestamp();
            (await client.GetAsync(url)).EnsureSuccessStatusCode();
            samples.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
        samples.Sort();
        return (samples[samples.Count / 2], samples[^1]);
    }

    // بيانات بحجم حقيقي بـ SQL مجموعي (لا صفّاً صفّاً): منتجات فعّالة بمتغيّر افتراضي وترجمة،
    // وطلبات مُسلَّمة بسطرين لكلٍّ منها موزّعة على المنتجات.
    private static async Task SeedAsync(ScratchApp app, int products, int orders)
    {
        await using var scope = app.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().UseTenant(await app.DefaultTenantAsync());
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();

        await ExecuteAsync(connection, $"""
            SET NOCOUNT ON;
            DECLARE @now datetime2 = SYSUTCDATETIME();
            WITH n AS (SELECT TOP ({products}) ROW_NUMBER() OVER (ORDER BY (SELECT 1)) AS i
                       FROM sys.all_objects a CROSS JOIN sys.all_objects b)
            INSERT INTO [Categories] ([TenantId], [Slug], [IsActive], [SortOrder], [CreatedAt])
            SELECT 1, CONCAT(N'cat-', i), 1, 0, @now FROM n WHERE i <= 5;

            DECLARE @cat int = (SELECT MIN([Id]) FROM [Categories]);
            WITH n AS (SELECT TOP ({products}) ROW_NUMBER() OVER (ORDER BY (SELECT 1)) AS i
                       FROM sys.all_objects a CROSS JOIN sys.all_objects b)
            INSERT INTO [Products] ([TenantId], [CategoryId], [Slug], [Status], [CreatedAt])
            SELECT 1, @cat, CONCAT(N'p-', i), 1, @now FROM n;

            INSERT INTO [ProductTranslations] ([TenantId], [ProductId], [Culture], [Name], [CreatedAt])
            SELECT 1, [Id], N'ar', CONCAT(N'منتج ', [Id]), @now FROM [Products];

            INSERT INTO [ProductVariants] ([TenantId], [ProductId], [Price], [Currency], [IsDefault], [CreatedAt])
            SELECT 1, [Id], 10.0000, N'JOD', 1, @now FROM [Products];

            WITH n AS (SELECT TOP ({orders}) ROW_NUMBER() OVER (ORDER BY (SELECT 1)) AS i
                       FROM sys.all_objects a CROSS JOIN sys.all_objects b CROSS JOIN sys.all_objects c)
            INSERT INTO [Orders] ([TenantId], [CustomerId], [Status], [ShippingAddress], [BillingAddress],
                                  [OrderNumber], [TrackingToken], [Currency], [PlacedSubtotal], [PlacedTotal],
                                  [ShippingAmount], [PlacedAt], [CreatedAt])
            SELECT 1, (SELECT MIN([Id]) FROM [Customers]), 3, N'addr', N'addr', 100000 + i,
                   RIGHT(REPLICATE(N'0', 32) + CONVERT(nvarchar(32), i), 32),
                   N'JOD', 20, 20, 0, @now, @now
            FROM n;

            -- سطران لكل طلب على منتجَين مختلفين، موزّعان على الكتالوج كلّه.
            INSERT INTO [OrderItems] ([TenantId], [OrderId], [ProductId], [ProductName], [UnitPrice], [Currency], [Quantity], [CreatedAt])
            SELECT 1, o.[Id], p.[Id], N'x', 10.0000, N'JOD', 1 + (o.[Id] % 3), @now
            FROM [Orders] o
            JOIN [Products] p ON p.[Id] IN (
                (SELECT MIN([Id]) FROM [Products]) + (o.[Id] % {products}),
                (SELECT MIN([Id]) FROM [Products]) + ((o.[Id] + 7) % {products}));
            """);
    }

    private static async Task ExecuteAsync(System.Data.Common.DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 300;
        await command.ExecuteNonQueryAsync();
    }

    private sealed class ScratchApp : WebApplicationFactory<Program>, IAsyncDisposable
    {
        private readonly string _connectionString;
        private readonly List<string> _sql = [];

        public ScratchApp(string server) =>
            _connectionString = new SqlConnectionStringBuilder(server)
            {
                InitialCatalog = $"perf_{Guid.NewGuid():N}"[..24],
            }.ConnectionString;

        public HttpClient Client() => CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost/"),
        });

        public int SelectCount() => _sql.Count(s => s.Contains("SELECT", StringComparison.Ordinal));

        public string LastSelect() =>
            _sql.LastOrDefault(s => s.Contains("SELECT", StringComparison.Ordinal))?
                .Replace("\r", " ").Replace("\n", " ")[..Math.Min(600, _sql.Last().Length)] ?? "(none captured)";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Default", _connectionString);
            builder.UseSetting("Jwt:Key", "perf-tests-signing-key-0123456789abcdef0123456789abc");
            builder.UseSetting("Seed:DemoData", "false");
            builder.UseSetting("Inventory:SweepIntervalSeconds", "0");
            builder.UseSetting("Basket:CleanupIntervalMinutes", "0");
            builder.UseSetting("Notifications:DispatchIntervalSeconds", "0");
            builder.ConfigureLogging(l => l.AddProvider(new SqlCapture(_sql))
                .AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Information));
        }

        public async Task<TenantInfo> DefaultTenantAsync()
        {
            await using var scope = Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<ITenantDirectory>()
                .FindBySlugAsync(DbSeeder.DefaultTenantSlug) ?? throw new InvalidOperationException("no default store");
        }

        public new async ValueTask DisposeAsync()
        {
            try
            {
                await using var scope = Services.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureDeletedAsync();
            }
            catch (Exception) { }
            await base.DisposeAsync();
        }
    }

    private sealed class SqlCapture(List<string> sink) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new Sink(sink, categoryName);
        public void Dispose() { }

        private sealed class Sink(List<string> sink, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error,
                Func<TState, Exception?, string> formatter)
            {
                if (category.Contains("Database.Command", StringComparison.Ordinal))
                    lock (sink) sink.Add(formatter(state, error));
            }
        }
    }
}
