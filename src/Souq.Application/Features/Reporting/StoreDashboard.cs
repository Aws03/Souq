using MediatR;
using Souq.Application.Common.Auditing;
using Souq.Application.Common.Tenancy;

using FluentValidation;

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

// ============================================================================
// هامش المدّة **مع تغطيته** (C11) — والتغطية ليست زينة بل شرط صدق الرقم.
//
// التكلفة اختيارية لكل متغيّر، ولقطتها على سطر الطلب قد تكون غائبة (أسطرٌ سبقت العمود، أو منتج
// لم تُدخَل تكلفته). فلو حُسب الهامش على الإيراد كلّه لعُدَّت التكلفة المجهولة **صفراً** — وظهر
// لتاجرٍ لم يُدخل تكلفةً واحدة أنّ هامشه مئة بالمئة. هذا ليس نقصاً في الدقّة، هو اختلاق.
//
// فالمحسوب هنا هو هامش **ما نعرف تكلفته وحده**، و`CoverageRatio` يقول كم من الإيراد ذلك.
// الواجهة تعرض الهامش حين تكون التغطية ذات معنى، وتقول "غير متاح" حين لا تكون.
//
// KnownRevenue: إيراد الأسطر ذات التكلفة المعروفة · KnownCost: تكلفتها · GrossProfit: الفرق
// · CoverageRatio: KnownRevenue ÷ إيراد المدّة كلّه (صفر حين لا إيراد).
// ============================================================================
public sealed record MarginDto(
    decimal KnownRevenue, decimal KnownCost, decimal GrossProfit, decimal CoverageRatio);

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
    MarginDto Margin,
    int PendingOrders,
    int PendingRefunds,
    int TotalCustomers,
    int RepeatCustomers);

/// <summary>
/// حدود المدّة كما يحسبها الخادم: [From, To) بـ UTC — والمدّة السابقة المساوية لها طولاً.
/// الحدود **تُشتقّ من يوم المتجر المحلّي** ثم تُحوَّل إلى UTC (C11)، و<see cref="Zone"/> هي
/// المنطقة التي اشتُقّت بها، ويلزمها تجميعُ المنحنى ليقع على أيّام المتجر لا على أيّام UTC.
/// </summary>
public sealed record ReportWindow(
    DateTime From, DateTime To, DateTime PreviousFrom, bool GroupByMonth, StoreTimeZone Zone);

public interface IStoreReports
{
    Task<StoreDashboardDto> GetDashboardAsync(ReportRange range, ReportWindow window, CancellationToken ct);
}

// ============================================================================
// الاستعلام مُدقَّق (IAuditable) لأنه يعيش تحت Features.Reporting، وModuleAndContractRuleTests
// يشترط ذلك. وهو مقصود هنا لا مجرّد امتثال: قراءة أرقام أعمال المتجر حدثٌ يستحقّ التسجيل.
// ============================================================================
// ============================================================================
// المدى تعدادٌ يُربط من سلسلة الاستعلام، ولا شيء في الربط يرفض رقماً خارج التعداد (M15): `?range=99`
// يمرّ ويصل `switch` فيسقط على حالته الافتراضية. لا خطر أمني — الافتراضي آمن — لكنّ التاجر يرى أرقام
// مدىً لم يطلبه ويظنّها مدَاه. المُحقِّق يجعله 400 يقول ما الخطأ بدل صمتٍ يُضلّل.
// ============================================================================
public sealed class GetStoreDashboardQueryValidator : AbstractValidator<GetStoreDashboardQuery>
{
    public GetStoreDashboardQueryValidator() => RuleFor(x => x.Range).IsInEnum();
}

public sealed record GetStoreDashboardQuery(ReportRange Range = ReportRange.Last30Days)
    : IRequest<StoreDashboardDto>, IAuditable
{
    public AuditRecord ToAuditRecord() =>
        new("store.dashboard.viewed", Metadata: new Dictionary<string, object?> { ["range"] = Range.ToString() });
}

public sealed class GetStoreDashboardHandler : IRequestHandler<GetStoreDashboardQuery, StoreDashboardDto>
{
    private readonly IStoreReports _reports;
    private readonly ITenantContext _tenant;
    private readonly TimeProvider _clock;

    public GetStoreDashboardHandler(IStoreReports reports, ITenantContext tenant, TimeProvider clock)
    {
        _reports = reports; _tenant = tenant; _clock = clock;
    }

    public Task<StoreDashboardDto> Handle(GetStoreDashboardQuery query, CancellationToken ct) =>
        _reports.GetDashboardAsync(
            query.Range,
            WindowFor(query.Range, _clock.GetUtcNow().UtcDateTime,
                StoreTimeZone.Resolve(_tenant.RequireTenant().TimeZone)),
            ct);

    // ========================================================================
    // حدود المدّة تُحسب هنا من الساعة المحقونة لا في SQL ولا في المتصفّح:
    //   • المتصفّح لا يُرسل تواريخ، فلا مدى يُوسَّع من العميل ليجرّ جدولاً كاملاً.
    //   • [From, To) نصف مفتوح: يوم اليوم يشمل ما وقع حتى اللحظة بلا تداخل مع الغد.
    //   • المدّة السابقة مساوية في الطول وملاصقة، فالمقارنة تقارن مثيلاً بمثيل.
    //   • المدد التي تتجاوز ~13 أسبوعاً تُجمَّع شهرياً: 365 نقطة على منحنى عرضه 600 بكسل
    //     ليست معلومة أكثر، وهي حمولة أكبر.
    //
    // **واليوم يوم المتجر لا يوم UTC** (C11). كان `nowUtc.Date` هو "اليوم"، و`TenantInfo.TimeZone`
    // مُسقَطة في كل طلب منذ المرحلة 4 ولا يقرؤها أحد هنا — فتاجرٌ في عمّان (UTC+3) يسأل عن "اليوم"
    // فيُجاب عن نافذة تبدأ الثالثة فجراً بتوقيته وتنتهي الثالثة فجراً من الغد: مبيعات ليلته الأخيرة
    // في اليوم الخطأ، وأوّل ثلاث ساعات من يومه غائبة. الخطأ صامت تماماً — الأرقام معقولة، وهي
    // لمدّةٍ أخرى.
    //
    // الحساب كلّه **بالتوقيت المحلّي** ثم يُحوَّل مرّةً واحدة إلى UTC للاستعلام. والترتيب مهمّ:
    // طرحُ الأيام محلّياً ثم التحويل يُبقي الحدود على منتصف ليلٍ حقيقي حتى عبر تغيير التوقيت
    // الصيفي، بينما طرحُها بـ UTC ثم التحويل يزيحها ساعةً في اليوم الذي يتغيّر فيه.
    // ========================================================================
    public static ReportWindow WindowFor(ReportRange range, DateTime nowUtc, StoreTimeZone zone)
    {
        var today = zone.ToLocal(nowUtc).Date;
        var to = today.AddDays(1);
        var (from, byMonth) = range switch
        {
            ReportRange.Today => (today, false),
            ReportRange.Last7Days => (today.AddDays(-6), false),
            ReportRange.Last30Days => (today.AddDays(-29), false),
            ReportRange.Last90Days => (today.AddDays(-89), true),
            ReportRange.ThisYear => (new DateTime(today.Year, 1, 1), true),
            _ => (today.AddDays(-29), false),
        };

        // المدّة السابقة تُطرح **بالأيام المحلّية** لا بالتِّكّات: عدّ التِّكّات يُزيح بدايتها ساعةً
        // كلّما وقع تغيير توقيتٍ داخل إحدى المدّتين، فتُقارَن مدّة بمدّة أطول منها بساعة.
        var previousFrom = from.AddDays(-(int)(to - from).TotalDays);

        return new ReportWindow(zone.ToUtc(from), zone.ToUtc(to), zone.ToUtc(previousFrom), byMonth, zone);
    }
}
