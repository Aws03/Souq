using AwesomeAssertions;
using NSubstitute;
using Souq.Application.Common.Exceptions;
using Souq.Application.Features.Coupons.Redemptions;
using Souq.Application.Tests.TestDoubles;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Exceptions;
using Souq.Domain.Interfaces;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Tests.Coupons;

// استخدامات الكوبونات (المرحلة 10): الحجز يعيد القواعد كلها بحدّ العميل ويسجّل، التعارض يُعاد من قراءة جديدة، التأكيد بلا
// حفظ، والتحرير يعيد الاستخدام مرة واحدة فقط.
public class CouponRedemptionsTests
{
    private readonly ICouponRepository _coupons = Substitute.For<ICouponRepository>();
    private readonly ICouponRedemptionRepository _redemptions = Substitute.For<ICouponRedemptionRepository>();
    private readonly IUnitOfWork _uow = TestUnitOfWork.Create();

    private CouponRedemptions Service() => new(_coupons, _redemptions, _uow, new FixedClock());

    private static Coupon NewCoupon(int? perCustomer = null, int? maxUses = null) =>
        TestCatalog.WithId(new Coupon("SAVE10", DiscountType.Percentage, 10, null, null, maxUses, maxUsesPerCustomer: perCustomer), 5);

    private static readonly Money Subtotal = new(100, "JOD");
    private static readonly Money Discount = new(10, "JOD");

    [Fact]
    public async Task الحجز_يأخذ_استخداماً_ويسجّله_محجوزاً_للطلب()
    {
        var coupon = NewCoupon(perCustomer: 2);
        _coupons.GetByCodeAsync("SAVE10", Arg.Any<CancellationToken>()).Returns(coupon);
        _redemptions.CountActiveAsync(5, 3, Arg.Any<CancellationToken>()).Returns(1);

        await Service().ReserveAsync("SAVE10", orderId: 7, customerId: 3, Subtotal, Discount, CancellationToken.None);

        coupon.UsedCount.Should().Be(1);
        await _redemptions.Received(1).AddAsync(
            Arg.Is<CouponRedemption>(r => r.CouponId == 5 && r.OrderId == 7 && r.CustomerId == 3
                                          && r.Status == CouponRedemptionStatus.Reserved && r.Discount == Discount),
            Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task حدّ_العميل_المستنفد_يُرفض_بلا_حفظ()
    {
        var coupon = NewCoupon(perCustomer: 1);
        _coupons.GetByCodeAsync("SAVE10", Arg.Any<CancellationToken>()).Returns(coupon);
        _redemptions.CountActiveAsync(5, 3, Arg.Any<CancellationToken>()).Returns(1);

        var act = () => Service().ReserveAsync("SAVE10", 7, 3, Subtotal, Discount, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidCouponException>();
        coupon.UsedCount.Should().Be(0);
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        _redemptions.Received(1).Reset();
    }

    [Fact]
    public async Task تعارض_التزامن_يُعاد_من_قراءة_جديدة_فيرى_ما_التزمه_الفائز()
    {
        // الفائز أخذ آخر استخدام قبلنا: المحاولة الأولى تتعارض، والثانية تقرأ الكوبون مستنفداً فتُرفض برسالة الكيان.
        _coupons.GetByCodeAsync("SAVE10", Arg.Any<CancellationToken>()).Returns(NewCoupon(maxUses: 1), Exhausted());
        var saves = 0;
        _uow.When(u => u.SaveChangesAsync(Arg.Any<CancellationToken>()))
            .Do(_ => { if (saves++ == 0) throw new ConcurrencyConflictException(); });

        var act = () => Service().ReserveAsync("SAVE10", 7, 3, Subtotal, Discount, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidCouponException>();
        _redemptions.Received(2).Reset();
        await _coupons.Received(2).GetByCodeAsync("SAVE10", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task التحرير_يعيد_الاستخدام_مرة_واحدة_والتأكيد_بلا_حفظ()
    {
        var coupon = NewCoupon();
        coupon.Redeem(Subtotal, FixedClock.DefaultNow.UtcDateTime, 0);
        var redemption = new CouponRedemption(5, 7, 3, Discount);
        _redemptions.GetForOrderAsync(7, Arg.Any<CancellationToken>()).Returns(redemption);
        _coupons.GetByIdAsync(5, Arg.Any<CancellationToken>()).Returns(coupon);

        await Service().ConfirmAsync(7, CancellationToken.None);
        redemption.Status.Should().Be(CouponRedemptionStatus.Confirmed);
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());

        await Service().ReleaseAsync(7, CancellationToken.None);
        await Service().ReleaseAsync(7, CancellationToken.None);

        (redemption.Status, coupon.UsedCount).Should().Be((CouponRedemptionStatus.Released, 0));
        await _coupons.Received(1).GetByIdAsync(5, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task طلب_بلا_كوبون_لا_يُؤكَّد_ولا_يُحرَّر_شيئاً()
    {
        _redemptions.GetForOrderAsync(8, Arg.Any<CancellationToken>()).Returns((CouponRedemption?)null);

        await Service().ConfirmAsync(8, CancellationToken.None);
        await Service().ReleaseAsync(8, CancellationToken.None);

        await _coupons.DidNotReceive().GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    private static Coupon Exhausted()
    {
        var coupon = NewCoupon(maxUses: 1);
        coupon.Redeem(Subtotal, FixedClock.DefaultNow.UtcDateTime, 0);
        return coupon;
    }
}
