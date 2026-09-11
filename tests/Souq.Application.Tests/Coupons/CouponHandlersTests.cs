using AwesomeAssertions;
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

        var handler = new CreateCouponHandler(_coupons, _uow);
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

        var handler = new CreateCouponHandler(_coupons, _uow);
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

        var handler = new UpdateCouponHandler(_coupons, _uow);
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

        var handler = new UpdateCouponHandler(_coupons, _uow);
        var result = await handler.Handle(
            new UpdateCouponCommand(1, DiscountType.Percentage, 20, null, null, null, IsActive: false), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        coupon.IsActive.Should().BeFalse();
        coupon.Value.Should().Be(20);
    }
}
