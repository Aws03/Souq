using AwesomeAssertions;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Exceptions;
using Souq.Domain.ValueObjects;

namespace Souq.Domain.Tests;

// مصفوفة قواعد الكوبون (المرحلة 10، معيار الخروج): كل تركيبة من التفعيل × النافذة × الحدّ العام × حدّ العميل × الحدّ الأدنى
// — صالح فقط إن تحقّقت كلها، والرفض لا يأخذ استخداماً. ثم التقريب بخانات كل عملة، ودورة حياة الاستخدام.
public class CouponRuleMatrixTests
{
    private static readonly DateTime Now = new(2026, 9, 11, 10, 0, 0, DateTimeKind.Utc);

    public enum Window { Open, Before, Within, After }

    public static TheoryData<bool, Window, bool, bool, bool> Matrix()
    {
        var data = new TheoryData<bool, Window, bool, bool, bool>();
        foreach (var active in new[] { true, false })
            foreach (var window in Enum.GetValues<Window>())
                foreach (var globalExhausted in new[] { false, true })
                    foreach (var customerExhausted in new[] { false, true })
                        foreach (var belowMinimum in new[] { false, true })
                            data.Add(active, window, globalExhausted, customerExhausted, belowMinimum);
        return data;
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public void صالح_فقط_إن_تحقّقت_القواعد_كلها_والرفض_لا_يأخذ_استخداماً(
        bool active, Window window, bool globalExhausted, bool customerExhausted, bool belowMinimum)
    {
        var coupon = new Coupon("MATRIX", DiscountType.Percentage, 10, new Money(50, "JOD"), null, maxUses: 2, maxUsesPerCustomer: 1);
        if (globalExhausted)
        {
            coupon.Redeem(new Money(100, "JOD"), Now, customerUses: 0);
            coupon.Redeem(new Money(100, "JOD"), Now, customerUses: 0);
        }
        var (startsAt, expiresAt) = window switch
        {
            Window.Before => (Now.AddDays(1), Now.AddDays(2)),
            Window.Within => (Now.AddDays(-1), Now.AddDays(1)),
            Window.After => (Now.AddDays(-2), Now.AddDays(-1)),
            _ => ((DateTime?)null, (DateTime?)null),
        };
        coupon.UpdateDetails(DiscountType.Percentage, 10, new Money(50, "JOD"), expiresAt, 2, startsAt, maxUsesPerCustomer: 1);
        if (!active) coupon.Deactivate();

        var usedBefore = coupon.UsedCount;
        var redeem = () => coupon.Redeem(new Money(belowMinimum ? 30 : 100, "JOD"), Now, customerUses: customerExhausted ? 1 : 0);

        var usable = active && window is Window.Open or Window.Within && !globalExhausted && !customerExhausted && !belowMinimum;
        if (usable)
        {
            redeem.Should().NotThrow();
            coupon.UsedCount.Should().Be(usedBefore + 1);
        }
        else
        {
            redeem.Should().Throw<InvalidCouponException>();
            coupon.UsedCount.Should().Be(usedBefore);
        }
    }

    [Theory]
    [InlineData(15, 12.345, "JOD", 1.852)]    // ثلاث خانات للدينار
    [InlineData(15, 12.3, "USD", 1.85)]       // 1.845 — منتصف يُقرَّب بعيداً عن الصفر
    [InlineData(33, 10, "USD", 3.3)]
    [InlineData(100, 7.5, "JOD", 7.5)]        // لا يتجاوز الإجمالي
    public void الخصم_النسبي_يُقرَّب_بخانات_العملة(decimal percent, decimal subtotal, string currency, decimal expected)
    {
        var coupon = new Coupon("ROUND", DiscountType.Percentage, percent, null, null, null);

        coupon.CalculateDiscount(new Money(subtotal, currency)).Should().Be(new Money(expected, currency));
    }

    [Fact]
    public void النافذة_والحدود_تُحرس_عند_الإنشاء_والتعديل()
    {
        ((Action)(() => new Coupon("W", DiscountType.Percentage, 10, null, Now, null, startsAt: Now))).Should().Throw<InvalidCouponException>();
        ((Action)(() => new Coupon("P", DiscountType.Percentage, 10, null, null, maxUses: 1, maxUsesPerCustomer: 2)))
            .Should().Throw<InvalidCouponException>();
        ((Action)(() => new Coupon("Z", DiscountType.Percentage, 10, null, null, maxUses: 0))).Should().Throw<InvalidCouponException>();
        ((Action)(() => new Coupon("C", DiscountType.Percentage, 10, null, null, null, maxUsesPerCustomer: 0))).Should().Throw<InvalidCouponException>();

        var coupon = new Coupon("OK", DiscountType.Percentage, 10, null, null, null);
        ((Action)(() => coupon.UpdateDetails(DiscountType.Percentage, 10, null, Now, null, startsAt: Now.AddDays(1))))
            .Should().Throw<InvalidCouponException>();
        coupon.StartsAt.Should().BeNull("تعديل مرفوض لا يغيّر شيئاً");
    }

    [Fact]
    public void الإعادة_لا_تنزل_تحت_الصفر_ودورة_الاستخدام_مضمونة_التكرار()
    {
        var coupon = new Coupon("ONE", DiscountType.FixedAmount, 5, null, null, maxUses: 1);
        coupon.Redeem(new Money(20, "JOD"), Now, customerUses: 0);
        coupon.ReleaseUse();
        coupon.ReleaseUse();
        coupon.UsedCount.Should().Be(0);

        var redemption = new CouponRedemption(1, 2, 3, new Money(5, "JOD"));
        redemption.Status.Should().Be(CouponRedemptionStatus.Reserved);
        redemption.Confirm();
        redemption.Confirm();
        redemption.Status.Should().Be(CouponRedemptionStatus.Confirmed);
        redemption.Release().Should().BeTrue();
        redemption.Release().Should().BeFalse("تحرير مكرّر لا يعيد الاستخدام مرتين");
        ((Action)redemption.Confirm).Should().Throw<InvalidCouponException>();
        ((Action)(() => new CouponRedemption(0, 2, 3, new Money(5, "JOD")))).Should().Throw<InvalidCouponException>();
    }
}
