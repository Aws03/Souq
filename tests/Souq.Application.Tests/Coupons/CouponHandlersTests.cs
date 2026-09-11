using AwesomeAssertions;
using Souq.Application.Tests.TestDoubles;
using NSubstitute;
using Souq.Application.Features.Coupons.Commands;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Interfaces;

namespace Souq.Application.Tests.Coupons;

public class CouponHandlersTests
{
    private readonly ICouponRepository _coupons = Substitute.For<ICouponRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    [Fact]
    public async Task إنشاء_كوبون_برمز_مكرّر_يُرفض()
    {
        _coupons.GetByCodeAsync("SAVE10", Arg.Any<CancellationToken>())
            .Returns(new Coupon("SAVE10", DiscountType.Percentage, 10, null, null, null));

        var handler = new CreateCouponHandler(_coupons, TestTenant.Context(), _uow);
        var result = await handler.Handle(
            new CreateCouponCommand("SAVE10", DiscountType.Percentage, 15, null, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("DuplicateCode");
        await _coupons.DidNotReceive().AddAsync(Arg.Any<Coupon>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task إنشاء_كوبون_صالح_يُحفظ()
    {
        _coupons.GetByCodeAsync("NEW10", Arg.Any<CancellationToken>()).Returns((Coupon?)null);

        var handler = new CreateCouponHandler(_coupons, TestTenant.Context(), _uow);
        var result = await handler.Handle(
            new CreateCouponCommand("NEW10", DiscountType.Percentage, 10, null, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _coupons.Received(1).AddAsync(Arg.Any<Coupon>(), Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task تحديث_كوبون_غير_موجود_يُرفض()
    {
        _coupons.GetByIdAsync(99, Arg.Any<CancellationToken>()).Returns((Coupon?)null);

        var handler = new UpdateCouponHandler(_coupons, TestTenant.Context(), _uow);
        var result = await handler.Handle(
            new UpdateCouponCommand(99, DiscountType.Percentage, 10, null, null, null, true), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("NotFound");
    }

    [Fact]
    public async Task تحديث_كوبون_بتعطيله_يُطبَّق()
    {
        var coupon = new Coupon("SAVE10", DiscountType.Percentage, 10, null, null, null);
        _coupons.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(coupon);

        var handler = new UpdateCouponHandler(_coupons, TestTenant.Context(), _uow);
        var result = await handler.Handle(
            new UpdateCouponCommand(1, DiscountType.Percentage, 20, null, null, null, IsActive: false), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        coupon.IsActive.Should().BeFalse();
        coupon.Value.Should().Be(20);
    }

    [Fact]
    public async Task الإنشاء_يحفظ_النافذة_وحدّ_العميل()
    {
        _coupons.GetByCodeAsync("WIN", Arg.Any<CancellationToken>()).Returns((Coupon?)null);
        Coupon? added = null;
        _coupons.When(c => c.AddAsync(Arg.Any<Coupon>(), Arg.Any<CancellationToken>())).Do(call => added = call.Arg<Coupon>());
        var starts = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

        var result = await new CreateCouponHandler(_coupons, TestTenant.Context(), _uow).Handle(
            new CreateCouponCommand("WIN", DiscountType.FixedAmount, 5, null, starts.AddDays(10), 100, starts, 2), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        (added!.StartsAt, added.ExpiresAt, added.MaxUsesPerCustomer).Should().Be((starts, starts.AddDays(10), 2));
    }

    [Fact]
    public void المدقّق_يرفض_نافذة_معكوسة_وحدّ_عميل_غير_موجب()
    {
        var starts = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var validator = new CreateCouponValidator();

        validator.Validate(new CreateCouponCommand("X", DiscountType.Percentage, 10, null, starts, null, starts.AddDays(1))).IsValid.Should().BeFalse();
        validator.Validate(new CreateCouponCommand("X", DiscountType.Percentage, 10, null, null, null, null, 0)).IsValid.Should().BeFalse();
        validator.Validate(new CreateCouponCommand("X", DiscountType.Percentage, 10, null, starts.AddDays(1), 5, starts, 2)).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task الكوبون_المستخدم_لا_يُحذف_بل_يُعطَّل_وغير_المستخدم_يُحذف()
    {
        var redemptions = Substitute.For<ICouponRedemptionRepository>();
        var used = TestCatalog.WithId(new Coupon("USED", DiscountType.Percentage, 10, null, null, null), 1);
        var fresh = TestCatalog.WithId(new Coupon("FRESH", DiscountType.Percentage, 10, null, null, null), 2);
        _coupons.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(used);
        _coupons.GetByIdAsync(2, Arg.Any<CancellationToken>()).Returns(fresh);
        redemptions.AnyForCouponAsync(1, Arg.Any<CancellationToken>()).Returns(true);
        var handler = new DeleteCouponHandler(_coupons, redemptions, _uow);

        (await handler.Handle(new DeleteCouponCommand(1), CancellationToken.None)).ErrorCode.Should().Be("CouponInUse");
        (await handler.Handle(new DeleteCouponCommand(2), CancellationToken.None)).IsSuccess.Should().BeTrue();

        _coupons.DidNotReceive().Remove(used);
        _coupons.Received(1).Remove(fresh);
    }
}
