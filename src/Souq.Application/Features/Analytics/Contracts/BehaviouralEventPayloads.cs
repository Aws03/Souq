using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using Souq.Domain.Entities;

// **وموضعُها في `Contracts` مقصود:** وحدةٌ أخرى تسجّل حدثاً تبني حمولته، فالحمولةُ جزءٌ من عقد
// المصرف لا تفصيلٌ داخليّ. وأوّلُ مَن أثبت ذلك هو اختبارُ الحدود نفسه: معالجُ البحث في وحدة
// Catalog أشار إليها وهي خارج العقود، فاحمرّ الفحص — وهو محقّ.
namespace Souq.Application.Features.Analytics.Contracts;

// ============================================================================
// حمولاتُ الأحداث السلوكية: نوعٌ لكل اسم، برقم إصدار ([ADR-0050](0050) §2).
//
// **الغلاف ثابت والحمولة مُصدَّرة.** إضافةُ حقلٍ اختياري لا تكسر شيئاً؛ تغييرُ نوعِ حقلٍ يكسر،
// فيلزمه إصدارٌ جديد. **والصفوف القديمة تبقى بإصدارها ولا تُعاد كتابتها** — فالقارئ متسامح:
// يقرأ ما يعرف ويتجاهل ما لا يعرف.
//
// **ولقطةُ ما رآه المتسوّق تُكتب هنا لا تُقرأ لاحقاً.** السعر والعملة والفئة وحالة التوفّر ورتبةُ
// العنصر في القائمة — كلّها في الحمولة لحظةَ الحدث. والوصلُ إلى `Product` عند التحليل يعيد قيمةَ
// **اليوم** لا ما رآه المتسوّق يومها، فيصير تقريرُ الأمس يتغيّر كلّما صُحِّح سعرٌ.
//
// **والمال يتبع المجال: مبلغٌ بعملته، لا عددٌ عائم أبداً.** `decimal` هنا تكتبه System.Text.Json
// بقيمته بلا تقريب؛ ولو كان `double` لدخلت الفلوسُ حسابَ الفاصلة العائمة الثنائية، فتختلف تحليلاتُ
// التاجر عن طلباته هو.
// ============================================================================

// ── الحمولات ──────────────────────────────────────────────────────────────

// بحثٌ نُفِّذ. المعرّف نفسه في الغلاف؛ هنا ما يخصّ البحثَ وحده.
// CorrectedTo: ما بحث عنه الخادم فعلاً بعد التصحيح (مسار الاسترجاع في CatalogQueries) — بلا هذا
// يبدو بحثٌ نجح وقد كان الشكل مختلفاً عمّا كُتب.
public sealed record SearchExecutedPayload(
    string? Term, string? TermNormalized, int ResultCount, int Page, int PageSize,
    string? CorrectedTo = null, string? CategorySuggested = null);

// عنصرٌ ظهر في قائمة، بموضعه ولقطته. Position مبدؤها 1 كما يعدّها إنسان.
public sealed record ImpressionItem(
    int ProductId, int Position, decimal UnitPrice, string Currency, bool InStock, int? CategoryId = null);

// قائمةٌ عُرضت. ListId هويّة القائمة (`search`، `offers`، `category:12`، `related:34`) — وبلا هويّة
// القائمة وموضعِ العنصر فيها لا تُحتسب نسبةُ نقرٍ لأيّ رفٍّ أبداً، وهذان هما الحقلان اللذان لا
// يمكن استرجاعهما لاحقاً بأيّ حال.
public sealed record ListViewedPayload(string ListId, IReadOnlyList<ImpressionItem> Items)
{
    // سقفُ العناصر في الحدث الواحد: صفحةُ نتائج أطول من هذا لا تُقرأ كلّها، والحمولة لها حدّ.
    public const int MaxItems = 60;
}

public sealed record ListClickedPayload(string ListId, int ProductId, int Position);

public sealed record ItemViewedPayload(
    int ProductId, int? VariantId, decimal UnitPrice, string Currency, bool InStock, int? CategoryId = null,
    string? ListId = null, int? Position = null);

// ListId وPosition اختياريان: الإضافة من رفٍّ تُنسَب إليه، والإضافة من صفحة المنتج لا رفَّ لها.
public sealed record CartChangedPayload(
    int ProductId, int? VariantId, int Quantity, decimal UnitPrice, string Currency,
    string? ListId = null, int? Position = null);

public sealed record CheckoutStartedPayload(decimal Total, string Currency, int LineCount);

public sealed record OrderLineSnapshot(int ProductId, int? VariantId, int Quantity, decimal UnitPrice);

public sealed record OrderPlacedPayload(
    int OrderId, decimal Total, string Currency, IReadOnlyList<OrderLineSnapshot> Items);

public sealed record OrderRefundedPayload(int OrderId, decimal Amount, string Currency);

// ── السجلّ ────────────────────────────────────────────────────────────────

// ============================================================================
// الاسم ⇒ (نوع الحمولة، إصدارها الحالي). قائمةٌ مغلقة، كما في `NotificationMessageTypes`: لا
// يُحلّ نوعٌ اعتباطيّ من اسمٍ قادم من القاعدة، ولا يُكتب اسمٌ بحمولةٍ لا تخصّه.
//
// وكلُّ اسمٍ في `BehaviouralEventNames` له مدخلٌ هنا، ويحرس التطابقَ اختبار: اسمٌ يُضاف بلا حمولة
// يُسقَط كلُّ حدثٍ يحمله **بصمت**، وهو أسوأ من عدم إضافته.
// ============================================================================
public static class BehaviouralEventPayloads
{
    private static readonly IReadOnlyDictionary<string, (Type Type, int Version)> ByName =
        new Dictionary<string, (Type, int)>(StringComparer.Ordinal)
        {
            [BehaviouralEventNames.SearchExecuted] = (typeof(SearchExecutedPayload), 1),
            [BehaviouralEventNames.ListViewed] = (typeof(ListViewedPayload), 1),
            [BehaviouralEventNames.ListClicked] = (typeof(ListClickedPayload), 1),
            [BehaviouralEventNames.ItemViewed] = (typeof(ItemViewedPayload), 1),
            [BehaviouralEventNames.CartAdded] = (typeof(CartChangedPayload), 1),
            [BehaviouralEventNames.CartRemoved] = (typeof(CartChangedPayload), 1),
            [BehaviouralEventNames.CheckoutStarted] = (typeof(CheckoutStartedPayload), 1),
            [BehaviouralEventNames.OrderPlaced] = (typeof(OrderPlacedPayload), 1),
            [BehaviouralEventNames.OrderRefunded] = (typeof(OrderRefundedPayload), 1),
        };

    // النصوص العربية كما هي لا \uXXXX، والتعدادات بأسمائها — تماماً كصندوق الصادر، ولنفس السببين:
    // حدُّ الحمولة لا يُستهلك ستّة أضعاف، وإعادةُ ترتيب تعدادٍ لا تُغيّر معنى صفٍّ مكتوب.
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static IReadOnlyCollection<string> Names => (IReadOnlyCollection<string>)ByName.Keys;

    // الإصدار الحالي للاسم، أو null إن كان الاسم أو نوع الحمولة غير متطابقين. لا يرمي: المسار
    // كلّه لا يرمي، والمجهول يُسقَط ويُعَدّ.
    public static int? VersionFor(string name, object payload) =>
        ByName.TryGetValue(name, out var known) && known.Type == payload.GetType() ? known.Version : null;

    public static Type? TypeFor(string name) =>
        ByName.TryGetValue(name, out var known) ? known.Type : null;

    public static string Serialize(object payload) =>
        JsonSerializer.Serialize(payload, payload.GetType(), Json);

    // للقراءة عند التجميع: الاسم يحدّد النوع، فلا يُحلّ نوعٌ من نصٍّ في القاعدة.
    public static object? Deserialize(string name, string payload) =>
        TypeFor(name) is { } type ? JsonSerializer.Deserialize(payload, type, Json) : null;
}
