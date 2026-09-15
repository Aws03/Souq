using MediatR;
using Souq.Application.Common.Auditing;

namespace Souq.Application.Features.Reporting;

// ============================================================================
// لوحة مؤشّرات المتجر (وحدة Reporting — قراءة فقط).
//
// لماذا هنا لا في وحدة الطلبات؟ لأن السؤال يعبر الوحدات: الإيراد من الطلبات، والأكثر مبيعاً من
// أسطرها مع الكتالوج، والمخزون من Inventory، والعملاء من Customers. جوابٌ مشتقّ لا مالك له —
// وهذا تعريف هذه الوحدة (Reporting/README.md).
//
// وخلافاً لإحصاءات المنصّة، هذه القراءة **لا تتجاوز مرشّح المستأجر**: داخل نطاق متجر يضيّق
// المرشّح كل جدول من تلقائه. لا IgnoreQueryFilters هنا ولا معرّف متجر في أي طلب — المتجر
// يُحلّ من المضيف على الخادم (AGENTS.md §4).
//
// ── تعريفات الأرقام (مطلب "أمانة البيانات") ───────────────────────────────────
// كل رقم هنا له صيغة واحدة مكتوبة، وهي نفسها المعروضة للمستخدم في الواجهة:
//
//   طلب محسوب   = طلب مُثبَّت (PlacedAt ليس null) حالته Paid أو Shipped أو Delivered.
//                  Pending لم يُدفع بعد، وCancelled لم يكتمل — فلا يدخل أيّهما الإيراد.
//   الإيراد      = مجموع PlacedTotal للطلبات المحسوبة في المدّة (لقطة الفاتورة، لا حساب جديد).
//   المُستردّ     = مجموع RefundedAmount لدفعات تلك الطلبات.
//   صافي الإيراد = الإيراد − المُستردّ. يُنسب الاسترداد إلى **مدّة الطلب** لا مدّة الاسترداد،
//                  فيبقى صافي المدّة متّسقاً مع طلباتها؛ وهذا اختيار صريح لا افتراض.
//   متوسّط قيمة الطلب = الإيراد ÷ عدد الطلبات المحسوبة (صفر حين لا طلبات — لا قسمة على صفر).
//   عميل جديد    = عميل أُنشئ صفّه داخل المدّة (CreatedAt)، غير الممسوحين.
//   عميل مُعيد   = عميل له طلبان محسوبان أو أكثر **منذ بداية المتجر**، لا داخل المدّة وحدها:
//                  التكرار صفة علاقة لا صفة أسبوع.
//
// ما لا يُحسب هنا، ولا يُختلق:
//   • الربح وهامشه — لا يحمل المستودع أي تكلفة شراء (لا حقل Cost في المنتج ولا في المتغيّر).
//     إيرادٌ يُسمّى ربحاً كذبة محاسبية، فالمقياس غائب ومُعلَن غيابه في الواجهة.
//   • معدّل التحويل — لا تتبّع زيارات ولا جلسات في المنتج، فلا مقام للكسر.
// ============================================================================

/// <summary>مدّة زمنية مغلقة يحسبها الخادم من مفتاح نصّي — لا تواريخ حرّة من المتصفّح.</summary>
public enum ReportRange
{
    Today = 0,
    Last7Days = 1,
    Last30Days = 2,
    Last90Days = 3,
    ThisYear = 4,
}

/// <summary>نقطة واحدة على منحنى الزمن: يوم (أو شهر للمدد الطويلة) وإيراده وعدد طلباته.</summary>
public sealed record SalesPointDto(DateTime Bucket, decimal Revenue, int Orders);

/// <summary>منتج في ترتيب الأكثر مبيعاً — الاسم لقطة الطلب لا اسم المنتج الحالي.</summary>
public sealed record TopProductDto(int ProductId, string Name, int UnitsSold, decimal Revenue);

/// <summary>أداء فئة: مجموع أسطر طلباتها المحسوبة.</summary>
public sealed record CategoryPerformanceDto(int CategoryId, string Name, int UnitsSold, decimal Revenue);

/// <summary>مؤشّرات المدّة مع مثيلاتها من المدّة السابقة لها مباشرةً (للمقارنة والاتجاه).</summary>
public sealed record PeriodTotalsDto(
    decimal Revenue, decimal Refunds, decimal NetRevenue, int Orders, decimal AverageOrderValue, int NewCustomers);

/// <summary>حالة المخزون الآن (ليست تاريخية): سليم، منخفض، نافد.</summary>
public sealed record InventorySnapshotDto(int Healthy, int Low, int OutOfStock);

/// <summary>
/// كل ما تعرضه لوحة المدير في استجابة واحدة: طلبٌ واحد لا اثنا عشر.
/// Currency عملة المتجر — كل المبالغ بها، ولا تُخلط عملات لأن الطلب يجمّد عملته.
/// </summary>
public sealed record StoreDashboardDto(
    string Range,
    DateTime From,
    DateTime To,
    string Currency,
    PeriodTotalsDto Current,
    PeriodTotalsDto Previous,
    IReadOnlyList<SalesPointDto> Trend,
    IReadOnlyDictionary<string, int> OrdersByStatus,
    IReadOnlyList<TopProductDto> TopProducts,
    IReadOnlyList<CategoryPerformanceDto> TopCategories,
    InventorySnapshotDto Inventory,
    int PendingOrders,
    int PendingRefunds,
    int TotalCustomers,
    int RepeatCustomers);

/// <summary>حدود المدّة كما يحسبها الخادم: [From, To) والمدّة السابقة المساوية لها طولاً.</summary>
public sealed record ReportWindow(DateTime From, DateTime To, DateTime PreviousFrom, bool GroupByMonth);

public interface IStoreReports
{
    Task<StoreDashboardDto> GetDashboardAsync(ReportRange range, ReportWindow window, CancellationToken ct);
}

// ============================================================================
// الاستعلام مُدقَّق (IAuditable) لأنه يعيش تحت Features.Reporting، وModuleAndContractRuleTests
// يشترط ذلك. وهو مقصود هنا لا مجرّد امتثال: قراءة أرقام أعمال المتجر حدثٌ يستحقّ التسجيل.
// ============================================================================
public sealed record GetStoreDashboardQuery(ReportRange Range = ReportRange.Last30Days)
    : IRequest<StoreDashboardDto>, IAuditable
{
    public AuditRecord ToAuditRecord() =>
        new("store.dashboard.viewed", Metadata: new Dictionary<string, object?> { ["range"] = Range.ToString() });
}

public sealed class GetStoreDashboardHandler : IRequestHandler<GetStoreDashboardQuery, StoreDashboardDto>
{
    private readonly IStoreReports _reports;
    private readonly TimeProvider _clock;

    public GetStoreDashboardHandler(IStoreReports reports, TimeProvider clock)
    {
        _reports = reports; _clock = clock;
    }

    public Task<StoreDashboardDto> Handle(GetStoreDashboardQuery query, CancellationToken ct) =>
        _reports.GetDashboardAsync(query.Range, WindowFor(query.Range, _clock.GetUtcNow().UtcDateTime), ct);

    // ========================================================================
    // حدود المدّة تُحسب هنا من الساعة المحقونة لا في SQL ولا في المتصفّح:
    //   • المتصفّح لا يُرسل تواريخ، فلا مدى يُوسَّع من العميل ليجرّ جدولاً كاملاً.
    //   • [From, To) نصف مفتوح: يوم اليوم يشمل ما وقع حتى اللحظة بلا تداخل مع الغد.
    //   • المدّة السابقة مساوية في الطول وملاصقة، فالمقارنة تقارن مثيلاً بمثيل.
    //   • المدد التي تتجاوز ~13 أسبوعاً تُجمَّع شهرياً: 365 نقطة على منحنى عرضه 600 بكسل
    //     ليست معلومة أكثر، وهي حمولة أكبر.
    // ========================================================================
    public static ReportWindow WindowFor(ReportRange range, DateTime nowUtc)
    {
        var today = nowUtc.Date;
        var to = today.AddDays(1);
        var (from, byMonth) = range switch
        {
            ReportRange.Today => (today, false),
            ReportRange.Last7Days => (today.AddDays(-6), false),
            ReportRange.Last30Days => (today.AddDays(-29), false),
            ReportRange.Last90Days => (today.AddDays(-89), true),
            ReportRange.ThisYear => (new DateTime(today.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc), true),
            _ => (today.AddDays(-29), false),
        };
        return new ReportWindow(from, to, from.AddTicks(-(to - from).Ticks), byMonth);
    }
}
