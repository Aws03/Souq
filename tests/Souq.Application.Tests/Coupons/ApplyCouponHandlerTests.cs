using AwesomeAssertions;
using NSubstitute;
using Souq.Application.Features.Coupons.Queries;
using Souq.Application.Tests.TestDoubles;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Exceptions;
using Souq.Domain.Interfaces;

namespace Souq.Application.Tests.Coupons;

// معاينة الكوبون بعملة المتجر دائماً — كانت بعملة يرسلها العميل (Phase 2).
public class ApplyCouponHandlerTests
{
    private readonly ICouponRepository _coupons = Substitute.For<ICouponRepository>();

    [Fact]
    public async Task الخصم_يُحسب_بعملة_المتجر_وخاناتها()
    {
        _coupons.GetByCodeAsync("SAVE15", Arg.Any<CancellationToken>())
            .Returns(new Coupon("SAVE15", DiscountType.Percentage, 15, null, null, null));
        var handler = new ApplyCouponHandler(_coupons, TestTenant.Context("USD"), new FixedClock());

        var result = await handler.Handle(new ApplyCouponQuery("SAVE15", 10.99m), CancellationToken.None);

        // 15% من 10.99 = 1.6485 ⇒ 1.65 بخانتَي الدولار (تقريب تجاري في مكان واحد — ADR-0014).
        result.Value!.DiscountAmount.Should().Be(1.65m);
        result.Value.NewTotal.Should().Be(9.34m);
    }

    [Fact]
    public async Task مبلغ_يتجاوز_خانات_عملة_المتجر_يُرفض()
    {
        _coupons.GetByCodeAsync("SAVE15", Arg.Any<CancellationToken>())
            .Returns(new Coupon("SAVE15", DiscountType.Percentage, 15, null, null, null));
        var handler = new ApplyCouponHandler(_coupons, TestTenant.Context("USD"), new FixedClock());

        var act = () => handler.Handle(new ApplyCouponQuery("SAVE15", 10.125m), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidMoneyException>();
    }
}
