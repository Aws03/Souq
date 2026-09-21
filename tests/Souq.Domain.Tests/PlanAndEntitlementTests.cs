using AwesomeAssertions;
using Souq.Domain.Exceptions;
using Souq.Domain.Platform;

namespace Souq.Domain.Tests;

// قواعد مستوى التحكّم التجاري (C1، ADR-0047): الخطة تُصدَّر وتُجمَّد بالنشر، والاستحقاق يُحسب
// **مغلقاً** — غياب المعلومة لا يمنح شيئاً — والاستثناء ينتهي ومنسوب ولا يمنع.
public class PlanAndEntitlementTests
{
    private static Plan DraftPlan(params string[] entitlements)
    {
        var plan = new Plan("Foundation", 1, "الخطة التأسيسية");
        plan.SetEntitlements(entitlements);
        return plan;
    }

    private static Plan PublishedPlan(params string[] entitlements)
    {
        var plan = DraftPlan(entitlements);
        plan.Publish();
        return plan;
    }

    // ── الخطة ───────────────────────────────────────────────────────────────

    [Fact]
    public void الخطة_الجديدة_مسوّدة_بمعرّف_مطبَّع_ولا_تمنح_شيئاً()
    {
        var plan = new Plan("Foundation", 1, "الخطة التأسيسية");

        plan.Status.Should().Be(PlanStatus.Draft);
        plan.Code.Should().Be("foundation");
        plan.Version.Should().Be(1);
        plan.Grants.Should().BeEmpty("الخطة تسمّي ما تمنحه — ولا شيء يُمنح بالغياب");
    }

    [Theory]
    [InlineData("a")]
    [InlineData("has space")]
    [InlineData("-leading")]
    [InlineData("trailing-")]
    [InlineData("under_score")]
    public void معرّف_خطة_غير_صالح_يُرفض(string code) =>
        FluentActions.Invoking(() => new Plan(code, 1, "خطة")).Should().Throw<InvalidPlanException>();

    [Fact]
    public void إصدار_أقل_من_واحد_يُرفض() =>
        FluentActions.Invoking(() => new Plan("foundation", 0, "خطة")).Should().Throw<InvalidPlanException>();

    [Fact]
    public void استحقاق_غير_معروف_يُرفض_في_الخطة_ولا_يُتجاهل()
    {
        // عكس StoreModules.Parse المتسامحة عمداً: الخطة عقد، وتجاهل بندٍ فيها بصمت بيعُ قدرة لا تُمنح.
        var plan = new Plan("foundation", 1, "خطة");

        FluentActions.Invoking(() => plan.SetEntitlements([StoreModules.Reviews, "nope"]))
            .Should().Throw<InvalidPlanException>();
        plan.Grants.Should().BeEmpty();
    }

    [Fact]
    public void الاستحقاقات_تُطبَّع_وتُرتَّب_ويُزال_تكرارها()
    {
        var plan = DraftPlan(" REVIEWS ", StoreModules.Reviews, StoreModules.Promotions);

        plan.Entitlements.Select(e => e.Entitlement)
            .Should().Equal(StoreModules.Promotions, StoreModules.Reviews);
    }

    [Fact]
    public void الخطة_المنشورة_لا_تُعدَّل_شروطها()
    {
        // المشترك يحتفظ بالشروط التي اشترك عليها: التعديل إصدارٌ جديد لا تحرير للقائم.
        var plan = PublishedPlan(StoreModules.Reviews);

        FluentActions.Invoking(() => plan.SetEntitlements([StoreModules.Wishlist])).Should().Throw<InvalidPlanException>();
        FluentActions.Invoking(() => plan.SetLimits([new Limit("products.max", 10)])).Should().Throw<InvalidPlanException>();
        FluentActions.Invoking(() => plan.Rename("اسم آخر")).Should().Throw<InvalidPlanException>();
        plan.Grants.Should().BeEquivalentTo([StoreModules.Reviews]);
    }

    [Fact]
    public void النشر_من_المسوّدة_وحدها_والتقاعد_من_المنشورة_وحدها()
    {
        var plan = DraftPlan();
        FluentActions.Invoking(plan.Retire).Should().Throw<InvalidPlanException>();

        plan.Publish();
        plan.Status.Should().Be(PlanStatus.Published);
        FluentActions.Invoking(plan.Publish).Should().Throw<InvalidPlanException>();

        plan.Retire();
        plan.Status.Should().Be(PlanStatus.Retired);
    }

    [Fact]
    public void الحدّ_الغائب_غير_محدَّد_لا_صفر()
    {
        var plan = new Plan("foundation", 1, "خطة");
        plan.SetLimits([new Limit("Catalog.Products", 500)]);

        plan.LimitFor(LimitNames.CatalogProducts).Should().Be(500);

        // C2 أجابه (ADR-0054): الغياب = **غير مقيَّد**، لا صفر. والجواب يُفرَض في TenantInfo.LimitFor،
        // وهذه الطبقة تبقى كما كانت: الكتالوج يقول ما حملته الخطة لا ما يعنيه غيابه.
        plan.LimitFor(LimitNames.StaffSeats).Should().BeNull();
    }

    [Fact]
    public void حدّ_سالب_أو_باسم_مكرَّر_يُرفض()
    {
        FluentActions.Invoking(() => new Limit(LimitNames.CatalogProducts, -1)).Should().Throw<InvalidPlanException>();

        var plan = new Plan("foundation", 1, "خطة");
        FluentActions.Invoking(() => plan.SetLimits(
                [new Limit("catalog.products", 1), new Limit("Catalog.Products", 2)]))
            .Should().Throw<InvalidPlanException>();
    }

    // ============================================================================
    // C2 (ADR-0054) — الكتالوج المغلق. C1 كان يقبل أيّ اسمٍ سليم الشكل كي لا يُجيب قرار المالك
    // C-12 ضمناً؛ ولمّا وُجد الفرض صار الاسم المجهول **وعداً لا يُنفَّذ**: يُحمَل في العقد ولا
    // يمنع شيئاً. والرفض هنا نظير رفضِ استحقاقٍ مجهول في SetEntitlements، بالحجّة نفسها.
    // ============================================================================
    [Fact]
    public void حدّ_باسم_خارج_الكتالوج_يُرفض()
    {
        FluentActions.Invoking(() => new Limit("products.max", 500))
            .Should().Throw<InvalidPlanException>("اسمٌ لا قاعدة عدّ له حدٌّ يُباع ولا يُفرَض");

        var plan = new Plan("foundation", 1, "خطة");
        FluentActions.Invoking(() => plan.SetLimits([new Limit("orders.per.month", 10)]))
            .Should().Throw<InvalidPlanException>();

        // والشكل يُفحص قبل العضوية: نصٌّ مشوّه رسالته عن شكله لا عن عضويّته.
        FluentActions.Invoking(() => new Limit("Catalog Products!", 1)).Should().Throw<InvalidPlanException>();
    }

    [Fact]
    public void كل_اسم_في_الكتالوج_مطبَّع_ومعروف()
    {
        LimitNames.All.Should().OnlyHaveUniqueItems().And.NotBeEmpty();
        foreach (var name in LimitNames.All)
        {
            LimitNames.Normalize(name).Should().Be(name, "الأسماء المعلنة مطبَّعة أصلاً");
            LimitNames.IsKnown(name).Should().BeTrue();
        }

        LimitNames.IsKnown(null).Should().BeFalse();
        LimitNames.IsKnown("catalog.nonexistent").Should().BeFalse();
    }

    // ── الاستحقاق الفعّال: يفشل مغلقاً ──────────────────────────────────────

    [Fact]
    public void الاستحقاق_الفعّال_تقاطع_ما_يسمح_به_العقد_وما_فعّلته_المنصّة()
    {
        var granted = Entitlements.Granted([StoreModules.Promotions, StoreModules.Reviews], null);
        var enabled = StoreModules.Parse($"{StoreModules.Reviews},{StoreModules.Wishlist}");

        Entitlements.Effective(granted, enabled).Should().BeEquivalentTo([StoreModules.Reviews]);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void مدخل_غائب_يمنح_لا_شيء(bool grantedNull, bool enabledNull)
    {
        // خطة لم تُحلّ، أو لقطة بُنيت بلا وحدات: المجموعة الفارغة، لا "كل شيء".
        var granted = grantedNull ? null : Entitlements.Granted([StoreModules.Reviews], null);
        var enabled = enabledNull ? null : StoreModules.Parse(StoreModules.Reviews);

        Entitlements.Effective(granted, enabled).Should().BeEmpty();
    }

    [Fact]
    public void استثناء_الدعم_يوسّع_ما_يسمح_به_العقد_ولا_يتجاوز_مفتاح_المنصّة()
    {
        var granted = Entitlements.Granted([StoreModules.Reviews], [StoreModules.Promotions]);
        granted.Should().BeEquivalentTo([StoreModules.Reviews, StoreModules.Promotions]);

        // وحدة أطفأتها المنصّة تبقى مطفأة وإن منحها استثناء: الإطفاء تشغيلي والاستثناء تجاري.
        Entitlements.Effective(granted, StoreModules.Parse(StoreModules.Reviews))
            .Should().BeEquivalentTo([StoreModules.Reviews]);
    }

    [Fact]
    public void مفتاح_مجهول_في_أيّ_مدخل_لا_يمرّ()
    {
        var granted = Entitlements.Granted(["nope", StoreModules.Reviews], ["also-nope"]);

        granted.Should().BeEquivalentTo([StoreModules.Reviews]);
        Entitlements.Effective(new HashSet<string>(StringComparer.Ordinal) { "nope" },
            new HashSet<string>(StringComparer.Ordinal) { "nope" }).Should().BeEmpty();
    }

    // ── الاشتراك ────────────────────────────────────────────────────────────

    [Fact]
    public void لا_يُشترَك_إلا_على_خطة_منشورة()
    {
        var now = new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);
        var draft = DraftPlan(StoreModules.Reviews);

        FluentActions.Invoking(() => new Subscription(7, draft, now)).Should().Throw<InvalidSubscriptionException>();

        draft.Publish();
        var subscription = new Subscription(7, draft, now);
        subscription.Status.Should().Be(SubscriptionStatus.Active);
        subscription.TenantId.Should().Be(7);
        subscription.StartedAtUtc.Should().Be(now);
        subscription.EndedAtUtc.Should().BeNull();
    }

    [Fact]
    public void الخطة_المتقاعدة_لا_تُسنَد_لمتجر_جديد()
    {
        var now = new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);
        var plan = PublishedPlan(StoreModules.Reviews);
        var subscription = new Subscription(7, plan, now);

        plan.Retire();

        FluentActions.Invoking(() => subscription.ChangePlan(plan, now)).Should().Throw<InvalidSubscriptionException>();
        subscription.Status.Should().Be(SubscriptionStatus.Active, "المشترك القائم يبقى على خطته");
    }

    [Fact]
    public void الإلغاء_يختم_الانتهاء_ويحتمل_التكرار()
    {
        var now = new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);
        var subscription = new Subscription(7, PublishedPlan(), now);

        subscription.Cancel(now.AddDays(1));
        subscription.Status.Should().Be(SubscriptionStatus.Cancelled);
        subscription.EndedAtUtc.Should().Be(now.AddDays(1));

        subscription.Cancel(now.AddDays(2));
        subscription.EndedAtUtc.Should().Be(now.AddDays(1), "الإلغاء الثاني لا يزحزح تاريخ الأول");
    }

    // ── استثناء الاستحقاق ───────────────────────────────────────────────────

    private static readonly DateTime Now = new(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);

    private static EntitlementOverride NewOverride(DateTime? expires = null) =>
        new(7, StoreModules.Promotions, expires ?? Now.AddDays(7), 42, "تجربة مدفوعة للعميل", Now);

    [Fact]
    public void الاستثناء_ينتهي_ومنسوب_ومعلَّل()
    {
        var granted = NewOverride();

        granted.IsActiveAt(Now).Should().BeTrue();
        granted.IsActiveAt(Now.AddDays(8)).Should().BeFalse("ينتهي بنفسه — لا استثناء دائم");
        granted.GrantedByUserId.Should().Be(42);
        granted.Reason.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(EntitlementOverride.MaxDurationDays + 1)]
    public void مدّة_خارج_المدى_تُرفض(int days) =>
        FluentActions.Invoking(() => NewOverride(Now.AddDays(days))).Should().Throw<InvalidEntitlementOverrideException>();

    [Fact]
    public void استثناء_بلا_سبب_أو_بلا_نسبة_يُرفض()
    {
        FluentActions.Invoking(() => new EntitlementOverride(7, StoreModules.Reviews, Now.AddDays(1), 0, "سبب كافٍ", Now))
            .Should().Throw<InvalidEntitlementOverrideException>();
        FluentActions.Invoking(() => new EntitlementOverride(7, StoreModules.Reviews, Now.AddDays(1), 42, "قصير", Now))
            .Should().Throw<InvalidEntitlementOverrideException>();
    }

    [Fact]
    public void استثناء_على_استحقاق_مجهول_يُرفض() =>
        FluentActions.Invoking(() => new EntitlementOverride(7, "nope", Now.AddDays(1), 42, "سبب كافٍ", Now))
            .Should().Throw<InvalidPlanException>();

    [Fact]
    public void السحب_يُنهي_الاستثناء_فوراً_ويحتمل_التكرار()
    {
        var granted = NewOverride();

        granted.Revoke(Now.AddDays(1));
        granted.IsActiveAt(Now.AddDays(2)).Should().BeFalse();
        granted.RevokedAtUtc.Should().Be(Now.AddDays(1));

        granted.Revoke(Now.AddDays(3));
        granted.RevokedAtUtc.Should().Be(Now.AddDays(1));
    }
}
