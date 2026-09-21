using AwesomeAssertions;
using Souq.Domain.Entities;

namespace Souq.Domain.Tests;

// ============================================================================
// غلافُ الحدث السلوكي وتجميعاته (C9، ADR-0050).
//
// ما يستحقّ اختباراً هنا ليس بناءَ كائن، بل **ما يرفضه الكيان** — لأنّ المسار كلّه لا يرمي: ما
// يرفضه `For` يُسقَط ويُعَدّ، فلو قَبِل ما لا يُقرأ لَكُتب في الجدول ولم يُقرأ في أيّ لوحة، وذلك
// أسوأ من إسقاطه.
// ============================================================================
public class BehaviouralEventTests
{
    private const string Payload = """{"productId":1}""";

    private static BehaviouralEvent? Build(
        string name = BehaviouralEventNames.ItemViewed, int version = 1,
        string surface = BehaviouralSurfaces.Storefront, string culture = "ar", string? payload = Payload) =>
        BehaviouralEvent.For(Guid.Empty, name, version, surface, culture, payload!,
            new DateTime(2026, 9, 22, 10, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 22, 10, 0, 1, DateTimeKind.Utc));

    [Fact]
    public void الغلاف_المكتمل_يُبنى_ويُصكّ_معرّفه_إن_لم_يُمرَّر()
    {
        var evt = Build()!;

        evt.Name.Should().Be(BehaviouralEventNames.ItemViewed);
        evt.SchemaVersion.Should().Be(1);
        evt.EventId.Should().NotBe(Guid.Empty, "معرّف الحدث يُصكّ عند الالتقاط، فإعادةُ إرسالٍ لا تصنع صفّين");
        evt.OccurredAt.Should().BeBefore(evt.ReceivedAt, "الفرق بينهما هو تأخّر المسار، وقياسُه يحتاج الاثنين");
    }

    // القوائم مغلقة: اسمٌ أو سطحٌ لا يعرفه المجال يُرفض. القائمةُ المفتوحة تكتب في الجدول ما لا
    // تقرؤه أيّ لوحة — وهو الدرس نفسه الذي أغلق أسماءَ الحدود في ADR-0054.
    [Theory]
    [InlineData("search.performed")]
    [InlineData("")]
    [InlineData("SearchExecuted")]
    public void اسم_خارج_القائمة_المغلقة_يُرفض(string name) => Build(name: name).Should().BeNull();

    [Theory]
    [InlineData("api")]
    [InlineData("STOREFRONT")]
    [InlineData("")]
    public void سطح_خارج_القائمة_المغلقة_يُرفض(string surface) => Build(surface: surface).Should().BeNull();

    [Fact]
    public void إصدار_غير_موجب_يُرفض() => Build(version: 0).Should().BeNull();

    [Fact]
    public void حمولة_فارغة_أو_أطول_من_الحدّ_تُرفض()
    {
        Build(payload: "").Should().BeNull();
        Build(payload: new string('x', BehaviouralEvent.PayloadMaxLength + 1)).Should()
            .BeNull("حمولةٌ أطول من هذا ليست حدثاً بل مستنداً");
    }

    [Fact]
    public void المعرّفات_تُقصّ_إلى_حدّها_والفارغ_يصير_null()
    {
        var evt = BehaviouralEvent.For(
            Guid.NewGuid(), BehaviouralEventNames.CartAdded, 1, BehaviouralSurfaces.Storefront, "AR ", Payload,
            DateTime.UtcNow, DateTime.UtcNow,
            visitorId: new string('v', BehaviouralEvent.IdentifierMaxLength + 20),
            sessionId: "   ",
            searchExecutionId: Guid.Empty)!;

        evt.VisitorId!.Length.Should().Be(BehaviouralEvent.IdentifierMaxLength);
        evt.SessionId.Should().BeNull();
        evt.SearchExecutionId.Should().BeNull("Guid.Empty ليس معرّفاً، فلا يُخزَّن كأنّه معرّف");
        evt.Culture.Should().Be("ar");
    }

    // ============================================================================
    // **لا معرّف عميل في غلاف الحدث**، وهذا أهمّ ما في هذا الملفّ: الفصل هو ما يجعل طلبَ المحو
    // حذفاً من جدول واحد بدل إعادة كتابة مخزنٍ لا يُكتب إلا إضافةً (ADR-0050 §5).
    // ============================================================================
    [Fact]
    public void الحدث_لا_يحمل_معرّف_عميل_أبداً()
    {
        var properties = typeof(BehaviouralEvent).GetProperties().Select(p => p.Name).ToList();

        properties.Should().NotContain("CustomerId");
        properties.Should().NotContain("UserId");
        properties.Should().NotContain("Email");
        properties.Should().Contain("VisitorId", "الزائر المُعتِم وحده، والربطُ في جدول منفصل");
    }
}

public class AnalyticsRollupTests
{
    [Fact]
    public void ربط_الزائر_بالعميل_يرفض_الناقص()
    {
        var now = DateTime.UtcNow;
        VisitorIdentityLink.For("visitor-1", 7, now).Should().NotBeNull();
        VisitorIdentityLink.For("", 7, now).Should().BeNull();
        VisitorIdentityLink.For("   ", 7, now).Should().BeNull();
        VisitorIdentityLink.For("visitor-1", 0, now).Should().BeNull();
        VisitorIdentityLink.For(new string('v', BehaviouralEvent.IdentifierMaxLength + 1), 7, now).Should().BeNull();
    }

    // الزوج مُرتَّب دائماً: بلا ترتيبٍ يُخزَّن الزوج نفسه مرّتين ويُقرأ نصفَ عددٍ في كل اتجاه.
    [Fact]
    public void زوج_المنتجات_مُرتَّب_ولا_يُزاوَج_منتجٌ_بنفسه()
    {
        var day = new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc);

        var pair = ProductPairDaily.For(day, 9, 4, coViews: 2, coPurchases: 1)!;
        (pair.ProductIdLow, pair.ProductIdHigh).Should().Be((4, 9));

        var mirrored = ProductPairDaily.For(day, 4, 9, 2, 1)!;
        (mirrored.ProductIdLow, mirrored.ProductIdHigh).Should().Be((4, 9));

        ProductPairDaily.For(day, 4, 4, 1, 0).Should().BeNull();
        ProductPairDaily.For(day, 0, 4, 1, 0).Should().BeNull();
    }

    // ============================================================================
    // العلامة **تتقدّم ولا تتراجع**، وهو ما يحمي المسح من فتحٍ ثانٍ: إعادةُ تجميع يومٍ قديم
    // (تصحيحاً) لو أرجعت العلامة لأصبح كلُّ ما بعده قابلاً للمسح مرّةً أخرى — وقد مُسح أصلاً.
    // ============================================================================
    [Fact]
    public void علامة_التجميع_تتقدّم_ولا_تتراجع()
    {
        var state = AnalyticsRollupState.Empty();
        var now = DateTime.UtcNow;
        state.RolledUpThroughDay.Should().BeNull("بلا تجميعٍ لا يُمسح شيء");

        state.Advance(new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc), now);
        state.RolledUpThroughDay.Should().Be(new DateTime(2026, 9, 10));

        state.Advance(new DateTime(2026, 9, 12, 13, 45, 0, DateTimeKind.Utc), now);
        state.RolledUpThroughDay.Should().Be(new DateTime(2026, 9, 12), "الوقت يُقصّ إلى يومه");

        state.Advance(new DateTime(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc), now);
        state.RolledUpThroughDay.Should().Be(new DateTime(2026, 9, 12), "إعادةُ تجميع يومٍ قديم لا تُرجع العلامة");
    }
}
