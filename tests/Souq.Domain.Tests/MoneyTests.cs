using AwesomeAssertions;
using Souq.Domain.Exceptions;
using Souq.Domain.ValueObjects;

namespace Souq.Domain.Tests;

public class MoneyTests
{
    // مخالفات قواعد المال استثناء مجال (InvalidMoneyException) لا ArgumentException —
    // كي تترجمها الـ API إلى 400 بدل 500 (Phase 0 C11).
    [Fact]
    public void مبلغ_سالب_يُرفض()
    {
        var act = () => new Money(-1, "JOD");

        act.Should().Throw<InvalidMoneyException>();
    }

    [Fact]
    public void عملة_فارغة_تُرفض()
    {
        var act = () => new Money(10, "  ");

        act.Should().Throw<InvalidMoneyException>();
    }

    [Theory]
    [InlineData("JO")]
    [InlineData("J0D")]
    [InlineData("JODX")]
    public void رمز_عملة_غير_صالح_يُرفض(string currency)
    {
        var act = () => new Money(10, currency);

        act.Should().Throw<InvalidMoneyException>();
    }

    [Fact]
    public void رمز_العملة_يُطبَّع_لأحرف_كبيرة()
    {
        new Money(1, "jod").Currency.Should().Be("JOD");
    }

    [Fact]
    public void الدينار_يقبل_ثلاث_خانات_عشرية_بلا_تقريب()
    {
        // قبل Phase 1A كان عمود decimal(18,2) يقرّب 12.345 إلى 12.35 بصمت (C5).
        new Money(12.345m, "JOD").Amount.Should().Be(12.345m);
    }

    [Theory]
    [InlineData(12.3456, "JOD")]   // الدينار 3 خانات
    [InlineData(1.005, "USD")]     // الدولار خانتان
    [InlineData(10.5, "JPY")]      // الين بلا كسور
    public void مبلغ_بخانات_أكثر_من_العملة_يُرفض(decimal amount, string currency)
    {
        var act = () => new Money(amount, currency);

        act.Should().Throw<InvalidMoneyException>();
    }

    [Theory]
    [InlineData(1.85175, "JOD", 1.852)]   // 15% من 12.345 — تقريب تجاري للفلس
    [InlineData(0.0005, "JOD", 0.001)]    // نقطة المنتصف تُقرَّب بعيداً عن الصفر
    [InlineData(2.345, "USD", 2.35)]
    [InlineData(99.5, "JPY", 100)]
    public void FromCalculation_يقرّب_تجارياً_لخانات_العملة(decimal raw, string currency, decimal expected)
    {
        Money.FromCalculation(raw, currency).Amount.Should().Be(expected);
    }

    [Fact]
    public void Add_بنفس_العملة_يجمع_المبلغين()
    {
        var result = new Money(10, "JOD").Add(new Money(5, "JOD"));

        result.Amount.Should().Be(15);
        result.Currency.Should().Be("JOD");
    }

    [Fact]
    public void Add_بعملتين_مختلفتين_يُرفض()
    {
        var act = () => new Money(10, "JOD").Add(new Money(5, "USD"));

        act.Should().Throw<InvalidMoneyException>();
    }

    [Fact]
    public void Subtract_بنفس_العملة_يطرح_المبلغين()
    {
        var result = new Money(10, "JOD").Subtract(new Money(4, "JOD"));

        result.Amount.Should().Be(6);
        result.Currency.Should().Be("JOD");
    }

    [Fact]
    public void Subtract_بعملتين_مختلفتين_يُرفض()
    {
        var act = () => new Money(10, "JOD").Subtract(new Money(5, "USD"));

        act.Should().Throw<InvalidMoneyException>();
    }

    [Fact]
    public void Subtract_ناتج_سالب_يُرفض()
    {
        var act = () => new Money(5, "JOD").Subtract(new Money(10, "JOD"));

        act.Should().Throw<InvalidMoneyException>();
    }

    [Fact]
    public void Multiply_يضرب_المبلغ_بالكمية()
    {
        var result = new Money(10, "JOD").Multiply(3);

        result.Amount.Should().Be(30);
    }

    [Fact]
    public void Zero_يعيد_مبلغاً_صفرياً_بنفس_العملة()
    {
        var zero = Money.Zero("USD");

        zero.Amount.Should().Be(0);
        zero.Currency.Should().Be("USD");
    }

    [Fact]
    public void ToString_يعرض_خانات_العملة_الصغرى()
    {
        new Money(59.9m, "JOD").ToString().Should().Be("59.900 JOD");
        new Money(5m, "USD").ToString().Should().Be("5.00 USD");
    }

    [Fact]
    public void مبلغان_متساويان_بنفس_القيمة_والعملة_يُعتبران_متساويين()
    {
        // Money سجل (record) — المساواة بالقيمة لا بالهوية.
        new Money(10, "JOD").Should().Be(new Money(10, "JOD"));
    }
}
