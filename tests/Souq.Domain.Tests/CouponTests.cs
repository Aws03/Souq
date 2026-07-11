using FluentAssertions;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Exceptions;
using Souq.Domain.ValueObjects;

namespace Souq.Domain.Tests;

public class CouponTests
{
    [Fact]
    public void الإنشاء_يخزّن_الرمز_بأحرف_كبيرة_دائماً()
    {
        var coupon = new Coupon("save10", DiscountType.Percentage, 10, null, null, null);

        coupon.Code.Should().Be("SAVE10");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void نسبة_خصم_خارج_1_100_تُرفض(decimal value)
    {
        var act = () => new Coupon("BAD", DiscountType.Percentage, value, null, null, null);
        act.Should().Throw<InvalidCouponException>();
    }

    [Fact]
    public void مبلغ_ثابت_سالب_أو_صفر_يُرفض()
    {
        var act = () => new Coupon("BAD", DiscountType.FixedAmount, 0, null, null, null);
        act.Should().Throw<InvalidCouponException>();
    }

    [Fact]
    public void CalculateDiscount_نسبة_مئوية_تحسب_الصحيح()
    {
        var coupon = new Coupon("SAVE10", DiscountType.Percentage, 10, null, null, null);

        var discount = coupon.CalculateDiscount(new Money(100));

        discount.Amount.Should().Be(10);
    }

    [Fact]
    public void CalculateDiscount_مبلغ_ثابت_لا_يتجاوز_الإجمالي_الفرعي()
    {
        var coupon = new Coupon("BIG20", DiscountType.FixedAmount, 20, null, null, null);

        var discount = coupon.CalculateDiscount(new Money(15));

        discount.Amount.Should().Be(15); // لا خصم سالب على الإجمالي المتبقّي
    }

    [Fact]
    public void EnsureUsable_كوبون_معطّل_يُرفض()
    {
        var coupon = new Coupon("SAVE10", DiscountType.Percentage, 10, null, null, null);
        coupon.Deactivate();

        var act = () => coupon.EnsureUsable(new Money(100), DateTime.UtcNow);
        act.Should().Throw<InvalidCouponException>();
    }

    [Fact]
    public void EnsureUsable_كوبون_منتهي_الصلاحية_يُرفض()
    {
        var coupon = new Coupon("OLD10", DiscountType.Percentage, 10, null,
            expiresAt: DateTime.UtcNow.AddDays(-1), maxUses: null);

        var act = () => coupon.EnsureUsable(new Money(100), DateTime.UtcNow);
        act.Should().Throw<InvalidCouponException>();
    }

    [Fact]
    public void EnsureUsable_استُنفد_عدد_الاستخدامات_يُرفض()
    {
        var coupon = new Coupon("ONE10", DiscountType.Percentage, 10, null, null, maxUses: 1);
        coupon.IncrementUsage();

        var act = () => coupon.EnsureUsable(new Money(100), DateTime.UtcNow);
        act.Should().Throw<InvalidCouponException>();
    }

    [Fact]
    public void EnsureUsable_دون_الحد_الأدنى_للطلب_يُرفض()
    {
        var coupon = new Coupon("MIN50", DiscountType.Percentage, 10, new Money(50), null, null);

        var act = () => coupon.EnsureUsable(new Money(30), DateTime.UtcNow);
        act.Should().Throw<InvalidCouponException>();
    }

    [Fact]
    public void EnsureUsable_كوبون_صالح_لا_يرمي()
    {
        var coupon = new Coupon("SAVE10", DiscountType.Percentage, 10, new Money(20), DateTime.UtcNow.AddDays(1), 5);

        var act = () => coupon.EnsureUsable(new Money(100), DateTime.UtcNow);
        act.Should().NotThrow();
    }

    [Fact]
    public void UpdateDetails_يرفض_قيمة_غير_منطقية_ولا_يغيّر_الحالة_القديمة()
    {
        var coupon = new Coupon("SAVE10", DiscountType.Percentage, 10, null, null, null);

        var act = () => coupon.UpdateDetails(DiscountType.Percentage, 200, null, null, null);

        act.Should().Throw<InvalidCouponException>();
        coupon.Value.Should().Be(10); // لم يتغيّر
    }
}
