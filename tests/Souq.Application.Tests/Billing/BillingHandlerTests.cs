using AwesomeAssertions;
using NSubstitute;
using Souq.Application.Common.Security;
using Souq.Application.Common.Tenancy;
using Souq.Application.Features.Billing;
using Souq.Domain.Interfaces;
using Souq.Domain.Platform;

namespace Souq.Application.Tests.Billing;

// تنسيق مستوى التحكّم التجاري (C1، ADR-0047/0053): كل كتابة تُبطل دليل المتاجر — وإلّا عمل المتجر
// بعقده القديم حتى دقيقة — والاستثناء لا يُكتب إذا كان لن يفعل شيئاً.
public class BillingHandlerTests
{
    private static readonly DateTime Now = new(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);

    private readonly ITenantRepository _tenants = Substitute.For<ITenantRepository>();
    private readonly IPlanRepository _plans = Substitute.For<IPlanRepository>();
    // C5 (ADR-0056): تسعيرُ الخطة يقرأ عملةَ فوترة المنصّة — بلا إعدادٍ لا سعر، والاختبارات هنا لا تُسعّر.
    private readonly IPlatformBillingSettingsRepository _billingSettings = Substitute.For<IPlatformBillingSettingsRepository>();
    private readonly ISubscriptionRepository _subscriptions = Substitute.For<ISubscriptionRepository>();
    private readonly IEntitlementOverrideRepository _overrides = Substitute.For<IEntitlementOverrideRepository>();
    private readonly ITenantDirectory _directory = Substitute.For<ITenantDirectory>();
    private readonly ICurrentUser _user = Substitute.For<ICurrentUser>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly TimeProvider _clock = new FixedClock(Now);

    private static Tenant Store(params string[] modules)
    {
        var tenant = new Tenant("Acme", "acme", "JOD", "ar", "Asia/Amman");
        tenant.SetModules(modules);
        return tenant;
    }

    private static Plan Published(params string[] entitlements)
    {
        var plan = new Plan("foundation", 1, "الخطة التأسيسية");
        plan.SetEntitlements(entitlements);
        plan.Publish();
        return plan;
    }

    // ── إسناد الخطة ─────────────────────────────────────────────────────────

    [Fact]
    public async Task إسناد_خطة_لمتجر_بلا_اشتراك_ينشئ_اشتراكاً_ويُبطل_الدليل()
    {
        _tenants.GetByIdAsync(7, Arg.Any<CancellationToken>()).Returns(Store(StoreModules.All.ToArray()));
        _plans.GetByIdAsync(3, Arg.Any<CancellationToken>()).Returns(Published(StoreModules.Reviews));

        var result = await Assigning().Handle(new AssignTenantPlanCommand(7, 3), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _subscriptions.Received(1).AddAsync(
            Arg.Is<Subscription>(s => s.TenantId == 7 && s.Status == SubscriptionStatus.Active), Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _directory.Received(1).InvalidateAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task إسناد_خطة_لمتجر_مشترك_يحوّل_اشتراكه_القائم_لا_ينشئ_ثانياً()
    {
        var existing = new Subscription(7, Published(StoreModules.Reviews), Now);
        _tenants.GetByIdAsync(7, Arg.Any<CancellationToken>()).Returns(Store(StoreModules.All.ToArray()));
        _plans.GetByIdAsync(4, Arg.Any<CancellationToken>()).Returns(Published(StoreModules.Wishlist));
        _subscriptions.FindByTenantAsync(7, Arg.Any<CancellationToken>()).Returns(existing);

        var result = await Assigning().Handle(new AssignTenantPlanCommand(7, 4), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _subscriptions.DidNotReceive().AddAsync(Arg.Any<Subscription>(), Arg.Any<CancellationToken>());
        await _directory.Received(1).InvalidateAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task متجر_غير_موجود_أو_خطة_غير_موجودة_ترفض_بلا_حفظ()
    {
        (await Assigning().Handle(new AssignTenantPlanCommand(7, 3), CancellationToken.None))
            .ErrorCode.Should().Be("NotFound");

        _tenants.GetByIdAsync(7, Arg.Any<CancellationToken>()).Returns(Store(StoreModules.All.ToArray()));
        (await Assigning().Handle(new AssignTenantPlanCommand(7, 3), CancellationToken.None))
            .ErrorCode.Should().Be("NotFound");

        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _directory.DidNotReceive().InvalidateAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task إلغاء_الخطة_يُبطل_الدليل_ولا_يلمس_حالة_المتجر()
    {
        var existing = new Subscription(7, Published(StoreModules.Reviews), Now);
        _subscriptions.FindByTenantAsync(7, Arg.Any<CancellationToken>()).Returns(existing);

        var result = await new CancelTenantPlanHandler(_subscriptions, _directory, _clock, _uow)
            .Handle(new CancelTenantPlanCommand(7), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        existing.Status.Should().Be(SubscriptionStatus.Cancelled);
        await _directory.Received(1).InvalidateAsync(Arg.Any<CancellationToken>());
    }

    // ── استثناءات الدعم ─────────────────────────────────────────────────────

    [Fact]
    public async Task منح_استثناء_ينسبه_لحساب_مانحه_ويُبطل_الدليل()
    {
        _tenants.GetByIdAsync(7, Arg.Any<CancellationToken>()).Returns(Store(StoreModules.All.ToArray()));
        _user.UserId.Returns(42);

        var result = await Granting().Handle(
            new GrantEntitlementOverrideCommand(7, StoreModules.Promotions, 14, "تجربة مدفوعة للعميل"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _overrides.Received(1).AddAsync(
            Arg.Is<EntitlementOverride>(o => o.TenantId == 7 && o.GrantedByUserId == 42
                                             && o.ExpiresAtUtc == Now.AddDays(14)),
            Arg.Any<CancellationToken>());
        await _directory.Received(1).InvalidateAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task استثناء_لوحدة_أطفأتها_المنصّة_يُرفض_بدل_أن_ينجح_بلا_أثر()
    {
        // الفعّال تقاطعٌ، فاستثناءٌ لوحدة مطفأة لا يفعل شيئاً. أمرٌ ينجح ولا يفعل شيئاً أسوأ من أمر يفشل.
        _tenants.GetByIdAsync(7, Arg.Any<CancellationToken>()).Returns(Store(StoreModules.Reviews));
        _user.UserId.Returns(42);

        var result = await Granting().Handle(
            new GrantEntitlementOverrideCommand(7, StoreModules.Promotions, 14, "تجربة مدفوعة للعميل"),
            CancellationToken.None);

        result.ErrorCode.Should().Be("ModuleSwitchedOff");
        await _overrides.DidNotReceive().AddAsync(Arg.Any<EntitlementOverride>(), Arg.Any<CancellationToken>());
        await _directory.DidNotReceive().InvalidateAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task استثناءان_ساريان_على_استحقاق_واحد_يُرفض_ثانيهما()
    {
        _tenants.GetByIdAsync(7, Arg.Any<CancellationToken>()).Returns(Store(StoreModules.All.ToArray()));
        _user.UserId.Returns(42);
        _overrides.FindActiveAsync(7, StoreModules.Promotions, Now, Arg.Any<CancellationToken>())
            .Returns(new EntitlementOverride(7, StoreModules.Promotions, Now.AddDays(3), 42, "سبب سابق", Now));

        var result = await Granting().Handle(
            new GrantEntitlementOverrideCommand(7, StoreModules.Promotions, 14, "سبب آخر"),
            CancellationToken.None);

        result.ErrorCode.Should().Be("OverrideAlreadyActive");
        await _overrides.DidNotReceive().AddAsync(Arg.Any<EntitlementOverride>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task سحب_استثناء_متجر_آخر_من_صفحة_هذا_المتجر_يجيب_غير_موجود()
    {
        // المتجر في المسار جزء من الهوية: الجدول بلا مرشّح، فالشرط يكتبه المعالج بيده.
        _overrides.GetByIdAsync(9, Arg.Any<CancellationToken>())
            .Returns(new EntitlementOverride(99, StoreModules.Reviews, Now.AddDays(3), 42, "لمتجر آخر", Now));

        var result = await new RevokeEntitlementOverrideHandler(_overrides, _directory, _clock, _uow)
            .Handle(new RevokeEntitlementOverrideCommand(7, 9), CancellationToken.None);

        result.ErrorCode.Should().Be("NotFound");
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── كتالوج الخطط ────────────────────────────────────────────────────────

    [Fact]
    public async Task إصدار_خطة_جديد_يأخذ_رقمه_من_المستودع_لا_من_الطلب()
    {
        _plans.NextVersionAsync("growth", Arg.Any<CancellationToken>()).Returns(3);

        var result = await new CreatePlanVersionHandler(_plans, _billingSettings, _uow).Handle(
            new CreatePlanVersionCommand("Growth", "خطة النمو", [StoreModules.Reviews], [new PlanLimitDto(LimitNames.CatalogProducts, 500)]),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _plans.Received(1).AddAsync(
            Arg.Is<Plan>(p => p.Code == "growth" && p.Version == 3 && p.Status == PlanStatus.Draft
                              && p.LimitFor(LimitNames.CatalogProducts) == 500),
            Arg.Any<CancellationToken>());
    }

    private AssignTenantPlanHandler Assigning() =>
        new(_tenants, _plans, _subscriptions, _directory, _clock, _uow);

    private GrantEntitlementOverrideHandler Granting() =>
        new(_tenants, _overrides, _directory, _user, _clock, _uow);

    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedClock(DateTime utcNow) => _now = new DateTimeOffset(utcNow);
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
