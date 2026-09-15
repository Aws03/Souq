using Microsoft.EntityFrameworkCore;
using Souq.Application.Common.Tenancy;
using Souq.Application.Features.Reporting;
using Souq.Domain.Enums;

namespace Souq.Infrastructure.Persistence.Queries;

// ============================================================================
// StoreReportQueries — أرقام لوحة متجر واحد.
//
// ثلاث قواعد تحكم هذا الملف:
//
//  1) **لا تجاوز لمرشّح المستأجر.** بخلاف PlatformQueries، كل استعلام هنا يمرّ بالمرشّح العادي،
//     فيستحيل أن يتسرّب صفّ من متجر إلى لوحة متجر آخر حتى لو أخطأ هذا الملف. المتجر يأتي من
//     ITenantContext (المضيف)، لا من مُعامل يرسله المتصفّح.
//
//  2) **التجميع كلّه في SQL.** لا ToListAsync على الطلبات ثم جمع في الذاكرة: متجرٌ بمئة ألف طلب
//     يجب أن يكلّف اللوحة نفس ما يكلّفه متجر بمئة. ما يعود من قاعدة البيانات هو النتيجة النهائية
//     (مجاميع، وأعلى عشرة) لا المادة الخام.
//
//  3) **الطلب المحسوب معرَّف في مكان واحد** (CountedStatuses أدناه) وتستعمله كل الاستعلامات،
//     فلا يختلف تعريف "الإيراد" بين بطاقة ومنحنى في الشاشة نفسها.
// ============================================================================
internal sealed class StoreReportQueries : IStoreReports
{
    // الطلب الذي دخل الإيراد: مُثبَّت ومدفوع. Pending لم يُدفع، وCancelled لم يكتمل.
    private static readonly OrderStatus[] CountedStatuses =
        [OrderStatus.Paid, OrderStatus.Shipped, OrderStatus.Delivered];

    private const int TopCount = 8;

    private readonly AppDbContext _db;
    private readonly ITenantContext _tenant;

    public StoreReportQueries(AppDbContext db, ITenantContext tenant)
    {
        _db = db; _tenant = tenant;
    }

    public async Task<StoreDashboardDto> GetDashboardAsync(ReportRange range, ReportWindow window, CancellationToken ct)
    {
        var currency = _tenant.RequireTenant().Currency;

        var current = await TotalsAsync(window.From, window.To, ct);
        var previous = await TotalsAsync(window.PreviousFrom, window.From, ct);

        return new StoreDashboardDto(
            Range: range.ToString(),
            From: window.From,
            To: window.To,
            Currency: currency,
            Current: current,
            Previous: previous,
            Trend: await TrendAsync(window, ct),
            OrdersByStatus: await OrdersByStatusAsync(window.From, window.To, ct),
            TopProducts: await TopProductsAsync(window.From, window.To, ct),
            TopCategories: await TopCategoriesAsync(window.From, window.To, ct),
            Inventory: await InventoryAsync(ct),
            PendingOrders: await PendingOrdersAsync(ct),
            PendingRefunds: await PendingRefundsAsync(ct),
            TotalCustomers: await _db.Customers.AsNoTracking().CountAsync(c => c.ErasedAt == null, ct),
            RepeatCustomers: await RepeatCustomersAsync(ct));
    }

    // الطلبات المحسوبة في [from, to) — الأساس المشترك لكل مبلغ في اللوحة.
    private IQueryable<Domain.Entities.Order> CountedOrders(DateTime from, DateTime to) =>
        _db.Orders.AsNoTracking()
            .Where(o => o.PlacedAt != null && o.PlacedAt >= from && o.PlacedAt < to
                        && CountedStatuses.Contains(o.Status));

    private async Task<PeriodTotalsDto> TotalsAsync(DateTime from, DateTime to, CancellationToken ct)
    {
        var counted = CountedOrders(from, to);

        // استعلام واحد لمجموعين وعدّ: SQL يجمعها في مسحٍ واحد.
        var totals = await counted
            .GroupBy(_ => 1)
            .Select(g => new { Revenue = g.Sum(o => o.PlacedTotal), Orders = g.Count() })
            .FirstOrDefaultAsync(ct);

        var revenue = totals?.Revenue ?? 0m;
        var orders = totals?.Orders ?? 0;

        // الاسترداد يُنسب إلى مدّة الطلب لا مدّة الاسترداد (موثَّق في StoreDashboard.cs).
        var refunds = await _db.Payments.AsNoTracking()
            .Where(p => counted.Any(o => o.Id == p.OrderId))
            .SumAsync(p => (decimal?)p.RefundedAmount, ct) ?? 0m;

        var newCustomers = await _db.Customers.AsNoTracking()
            .CountAsync(c => c.ErasedAt == null && c.CreatedAt >= from && c.CreatedAt < to, ct);

        return new PeriodTotalsDto(
            Revenue: revenue,
            Refunds: refunds,
            NetRevenue: revenue - refunds,
            Orders: orders,
            AverageOrderValue: orders == 0 ? 0m : decimal.Round(revenue / orders, 2),
            NewCustomers: newCustomers);
    }

    // ========================================================================
    // المنحنى: تجميع في SQL بالسنة/الشهر/اليوم، ثم **ملء الفجوات** في الذاكرة على المدّة
    // المطلوبة. الملء ضروري لا تجميلي: يومٌ بلا طلبات نقطة صفر على المنحنى، وحذفه يجعل
    // الخطّ يقفز فيبدو أسبوعٌ ميّت كأنه متّصل.
    // ========================================================================
    private async Task<IReadOnlyList<SalesPointDto>> TrendAsync(ReportWindow window, CancellationToken ct)
    {
        var counted = CountedOrders(window.From, window.To);

        var rows = window.GroupByMonth
            ? await counted
                .GroupBy(o => new { o.PlacedAt!.Value.Year, o.PlacedAt!.Value.Month })
                .Select(g => new { g.Key.Year, g.Key.Month, Day = 1, Revenue = g.Sum(o => o.PlacedTotal), Orders = g.Count() })
                .ToListAsync(ct)
            : await counted
                .GroupBy(o => new { o.PlacedAt!.Value.Year, o.PlacedAt!.Value.Month, o.PlacedAt!.Value.Day })
                .Select(g => new { g.Key.Year, g.Key.Month, g.Key.Day, Revenue = g.Sum(o => o.PlacedTotal), Orders = g.Count() })
                .ToListAsync(ct);

        var byBucket = rows.ToDictionary(
            r => new DateTime(r.Year, r.Month, r.Day, 0, 0, 0, DateTimeKind.Utc),
            r => (r.Revenue, r.Orders));

        var points = new List<SalesPointDto>();
        var cursor = window.GroupByMonth
            ? new DateTime(window.From.Year, window.From.Month, 1, 0, 0, 0, DateTimeKind.Utc)
            : window.From;

        while (cursor < window.To)
        {
            var found = byBucket.TryGetValue(cursor, out var value);
            points.Add(new SalesPointDto(cursor, found ? value.Revenue : 0m, found ? value.Orders : 0));
            cursor = window.GroupByMonth ? cursor.AddMonths(1) : cursor.AddDays(1);
        }

        return points;
    }

    // كل حالة مذكورة ولو بصفر: جدول حالات ناقص يجعل "لا ملغاة" تبدو كمعلومة غائبة.
    private async Task<IReadOnlyDictionary<string, int>> OrdersByStatusAsync(DateTime from, DateTime to, CancellationToken ct)
    {
        var rows = await _db.Orders.AsNoTracking()
            .Where(o => o.PlacedAt != null && o.PlacedAt >= from && o.PlacedAt < to)
            .GroupBy(o => o.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return Enum.GetValues<OrderStatus>()
            .ToDictionary(s => s.ToString(), s => rows.FirstOrDefault(r => r.Status == s)?.Count ?? 0);
    }

    // أسطر الطلبات المحسوبة. تُقرأ عبر التجمّع (Order.Items) لا من جدول مستقلّ: مفتاح السطر
    // الخارجي مفتاح ظلّ يملكه التجمّع، وهذا ما يجعل السطر غير قابل للوصول خارج طلبه.
    private IQueryable<Domain.Entities.OrderItem> CountedItems(DateTime from, DateTime to) =>
        CountedOrders(from, to).SelectMany(o => o.Items);

    // الترتيب والقصّ يقعان على النوع المجهول لا على السجلّ: EF لا يترجم OrderBy على خاصّية
    // سجلٍّ أُنشئ داخل Select. النتيجة ثمانية صفوف تُحوَّل في الذاكرة — لا حمولة هنا.
    private async Task<IReadOnlyList<TopProductDto>> TopProductsAsync(DateTime from, DateTime to, CancellationToken ct)
    {
        var rows = await CountedItems(from, to)
            .GroupBy(i => new { i.ProductId, i.ProductName })
            .Select(g => new
            {
                g.Key.ProductId,
                g.Key.ProductName,
                Units = g.Sum(i => i.Quantity),
                Revenue = g.Sum(i => i.UnitPrice.Amount * i.Quantity),
            })
            .OrderByDescending(r => r.Revenue)
            .Take(TopCount)
            .ToListAsync(ct);

        // الاسم لقطة الطلب لا اسم المنتج الحالي: منتج أُعيدت تسميته أو أُرشف يبقى مقروءاً في تقرير أمسه.
        return rows.Select(r => new TopProductDto(r.ProductId, r.ProductName, r.Units, r.Revenue)).ToList();
    }

    // الفئة تأتي من المنتج الحالي: سطر لمنتج حُذف سجلّه لا فئة له، فيُترك خارج التقرير بدل نسبته لفئة خاطئة.
    //
    // والاسم اسمٌ مترجم لا Slug. كان التقرير يعرض "home" — وهو معرّف داخلي في الرابط، لا شيء
    // يراه مدير متجر عربي في تقريره. والسلسلة نفسها المستعملة في الكتالوج: ترجمة لغة المتجر
    // الافتراضية، فأيّ ترجمة موجودة، فالـSlug أخيراً — فئة بلا ترجمة إطلاقاً تبقى مميَّزة بشيء.
    private async Task<IReadOnlyList<CategoryPerformanceDto>> TopCategoriesAsync(DateTime from, DateTime to, CancellationToken ct)
    {
        var culture = _tenant.RequireTenant().DefaultCulture;

        var rows = await CountedItems(from, to)
            .Join(_db.Products, i => i.ProductId, p => p.Id, (i, p) => new { i, p.CategoryId })
            .Join(_db.Categories, x => x.CategoryId, c => c.Id, (x, c) => new
            {
                x.i,
                c.Id,
                Name = c.Translations.Where(t => t.Culture == culture).Select(t => t.Name).FirstOrDefault()
                    ?? c.Translations.OrderBy(t => t.Culture).Select(t => t.Name).FirstOrDefault()
                    ?? c.Slug,
            })
            .GroupBy(x => new { x.Id, x.Name })
            .Select(g => new
            {
                CategoryId = g.Key.Id,
                g.Key.Name,
                Units = g.Sum(x => x.i.Quantity),
                Revenue = g.Sum(x => x.i.UnitPrice.Amount * x.i.Quantity),
            })
            .OrderByDescending(r => r.Revenue)
            .Take(TopCount)
            .ToListAsync(ct);

        return rows.Select(r => new CategoryPerformanceDto(r.CategoryId, r.Name, r.Units, r.Revenue)).ToList();
    }

    // المخزون حالةٌ آنيّة لا تاريخية: المتاح = ما في اليد ناقص المحجوز (نفس تعريف وحدة Inventory).
    private async Task<InventorySnapshotDto> InventoryAsync(CancellationToken ct)
    {
        var rows = await _db.InventoryItems.AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Out = g.Count(i => i.OnHand - i.Reserved <= 0),
                Low = g.Count(i => i.OnHand - i.Reserved > 0 && i.OnHand - i.Reserved <= i.LowStockThreshold),
                Total = g.Count(),
            })
            .FirstOrDefaultAsync(ct);

        return rows is null
            ? new InventorySnapshotDto(0, 0, 0)
            : new InventorySnapshotDto(rows.Total - rows.Low - rows.Out, rows.Low, rows.Out);
    }

    // تنبيهات العمل: طلبات تنتظر الدفع، واستردادات لم تُسوَّ بعد — كلاهما "يحتاج انتباهاً الآن".
    private Task<int> PendingOrdersAsync(CancellationToken ct) =>
        _db.Orders.AsNoTracking().CountAsync(o => o.PlacedAt != null && o.Status == OrderStatus.Pending, ct);

    private Task<int> PendingRefundsAsync(CancellationToken ct) =>
        _db.Payments.AsNoTracking().CountAsync(p => p.PendingRefundAmount > 0, ct);

    // العميل المُعيد: طلبان محسوبان أو أكثر منذ بداية المتجر. صفة علاقة لا صفة مدّة.
    private Task<int> RepeatCustomersAsync(CancellationToken ct) =>
        _db.Orders.AsNoTracking()
            .Where(o => o.PlacedAt != null && CountedStatuses.Contains(o.Status))
            .GroupBy(o => o.CustomerId)
            .CountAsync(g => g.Count() > 1, ct);
}
