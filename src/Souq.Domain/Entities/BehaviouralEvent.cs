using Souq.Domain.Common;
using Souq.Domain.Interfaces;

namespace Souq.Domain.Entities;

// ============================================================================
// حدثٌ سلوكيّ واحد: ما فعله متسوّقٌ في متجرٍ، بلقطةِ ما رآه لحظةَ فعله ([ADR-0050](0050)).
//
// **الغلاف ثابت والحمولة مُصدَّرة.** الأعمدة هنا هي الغلاف: مَن ومتى وأين وبأيّ بحث. وما يخصّ
// نوعَ الحدث وحده يعيش في `Payload` كـ JSON برقم إصدار — فحقلٌ جديد في نوعٍ واحد لا يُغيّر جدولاً
// تكتب فيه كلُّ الأنواع، **والصفوف القديمة لا تُعاد كتابتها أبداً**: تبقى بإصدارها ويقرؤها
// المستهلك متسامحاً.
//
// **ولا `CustomerId` هنا، وهذا أهمّ قرار في الملفّ.** الحدث يحمل `VisitorId` مُعتِماً وحده،
// والربطُ بين الزائر والعميل يعيش في جدول منفصل (`VisitorIdentityLink`). فطلبُ محوٍ يصير حذفاً
// من مكان واحد بدل إعادة كتابة مخزنٍ لا يُكتب إلا إضافةً — وهو النمط الذي تُفشله المحاولةُ
// المتأخّرة: مَن يُدخل معرّف العميل في الصفوف أوّلاً لا يستطيع فصله لاحقاً.
//
// **الضمان أضعفُ من ضمان صندوق الصادر عمداً: «مرّة على الأكثر».** المسار غير حاجز ويُسقط عند
// الامتلاء. ولذلك **لا يُحتسب منه مالٌ ولا فاتورة** — حدثٌ قابل للإسقاط لا يصلح أساساً لفوترة،
// والأحداث القابلة للفوترة صفوفٌ دائمة بمفتاح تعطيلٍ حتميّ (ADR-0050 §1).
// ============================================================================
public class BehaviouralEvent : Entity, ITenantOwned
{
    public const int NameMaxLength = 40;
    public const int IdentifierMaxLength = 64;
    public const int SurfaceMaxLength = 20;
    public const int CultureMaxLength = 10;
    public const int PayloadMaxLength = 4000;

    public int TenantId { get; private set; }

    // معرّف الحدث نفسه: يُولَّد عند الالتقاط لا عند الكتابة، فإعادةُ إرسالٍ لا تصنع صفّين.
    public Guid EventId { get; private set; }

    public string Name { get; private set; } = default!;
    public int SchemaVersion { get; private set; }

    // متى وقع (لحظةُ الفعل) ومتى وصل (لحظةُ الكتابة). الفرق بينهما هو تأخّر المسار، وقياسه يحتاج
    // الاثنين — وبواحدٍ منهما لا يُعرف أبداً.
    public DateTime OccurredAt { get; private set; }
    public DateTime ReceivedAt { get; private set; }

    // زائرٌ مُعتِم مُولَّد في الخادم، لا بريد ولا بصمة جهاز ولا معرّف إعلاني ولا مشتقٌّ من IP.
    // null ⇒ الالتقاط يعمل بلا معرّف زائر (وهو جواب C-08 = لا، محفوظاً في الشكل نفسه).
    public string? VisitorId { get; private set; }

    // جلسةٌ تُقطَّع عند الكتابة بخمولٍ ثلاثين دقيقة (ADR-0050 §4): استنباطُها لاحقاً من الطوابع
    // ينكسر يوم يتغيّر التعريف، أو يوم يُمسح ما قبلها.
    public string? SessionId { get; private set; }

    // معرّف تنفيذ البحث: يُصكّ لحظة الاستعلام ويُعاد مع النتائج ويُردّ مع كل نقرةٍ وإضافةٍ وشراء.
    // بلا هذا لا تُنسَب مبيعةٌ إلى البحث الذي أنتجها — والقُرب الزمنيّ ليس نسبةً.
    public Guid? SearchExecutionId { get; private set; }

    // معرّف الطلب (X-Correlation-Id) لوصل الحدث بسجلّ الخادم عند التشخيص.
    public string? CorrelationId { get; private set; }

    // السطح الذي وقع عليه: متجر، لوحة تاجر، منصّة. سطحٌ واحد لكل حدث، ويُقرأ لا يُخمَّن.
    public string Surface { get; private set; } = default!;

    public string Culture { get; private set; } = default!;

    // حمولة النوع، JSON بإصدارها. محدودة الطول: حمولةٌ أطول من هذا ليست حدثاً بل مستنداً.
    public string Payload { get; private set; } = default!;

    private BehaviouralEvent() { }

    // يُبنى بلقطةٍ مكتملة أو لا يُبنى: اسمٌ من القائمة المغلقة، وسطحٌ معروف، وحمولةٌ بحدّها.
    // null ⇒ الحدث يُهمَل بصمت في المسار (وهو مسارٌ لا يرمي أبداً)، ويُعَدّ مُسقَطاً.
    public static BehaviouralEvent? For(
        Guid eventId, string name, int schemaVersion, string surface, string culture, string payload,
        DateTime occurredAt, DateTime receivedAt,
        string? visitorId = null, string? sessionId = null, Guid? searchExecutionId = null, string? correlationId = null)
    {
        if (!BehaviouralEventNames.Contains(name)) return null;
        if (schemaVersion < 1) return null;
        if (string.IsNullOrWhiteSpace(payload) || payload.Length > PayloadMaxLength) return null;
        if (!BehaviouralSurfaces.Contains(surface)) return null;

        var language = (culture ?? "").Trim().ToLowerInvariant();
        if (language.Length == 0 || language.Length > CultureMaxLength) return null;

        return new BehaviouralEvent
        {
            EventId = eventId == Guid.Empty ? Guid.CreateVersion7() : eventId,
            Name = name,
            SchemaVersion = schemaVersion,
            Surface = surface,
            Culture = language,
            Payload = payload,
            OccurredAt = occurredAt,
            ReceivedAt = receivedAt,
            VisitorId = Trim(visitorId),
            SessionId = Trim(sessionId),
            SearchExecutionId = searchExecutionId == Guid.Empty ? null : searchExecutionId,
            CorrelationId = Trim(correlationId),
        };
    }

    private static string? Trim(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed.Length > IdentifierMaxLength
            ? trimmed[..IdentifierMaxLength]
            : trimmed;
    }
}

// ============================================================================
// أسماء الأحداث: **قائمة مغلقة**، للسبب الذي أغلق به ADR-0054 أسماءَ الحدود — اسمٌ لا يعرفه أحد
// يُكتب في الجدول ولا يُقرأ في أيّ لوحة، فيصير قياساً يظنّ التاجر أنه يملكه ولا يملكه.
//
// والمفردات هذه ليست اختراعاً: هي ما التقت عليه ثلاث منصّات تحليلٍ مستقلّة، فاعتمادُها لا يربط
// سوق بأيٍّ منها ويجعل محوّلاً إلى أيٍّ منها لاحقاً ترجمةَ أسماء لا إعادةَ تصميم (ADR-0050 §Context).
// ============================================================================
public static class BehaviouralEventNames
{
    public const string SearchExecuted = "search.executed";
    public const string ListViewed = "list.viewed";
    public const string ListClicked = "list.clicked";
    public const string ItemViewed = "item.viewed";
    public const string CartAdded = "cart.added";
    public const string CartRemoved = "cart.removed";
    public const string CheckoutStarted = "checkout.started";
    public const string OrderPlaced = "order.placed";
    public const string OrderRefunded = "order.refunded";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        SearchExecuted, ListViewed, ListClicked, ItemViewed,
        CartAdded, CartRemoved, CheckoutStarted, OrderPlaced, OrderRefunded,
    };

    public static bool Contains(string? name) => name is not null && All.Contains(name);
}

// السطح: متجرٌ يراه متسوّق، لوحةُ تاجر، أو منصّة. مغلقة كالأسماء ولنفس السبب.
public static class BehaviouralSurfaces
{
    public const string Storefront = "storefront";
    public const string Admin = "admin";
    public const string Platform = "platform";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.Ordinal) { Storefront, Admin, Platform };

    public static bool Contains(string? surface) => surface is not null && All.Contains(surface);
}
