using FluentAssertions;
using Souq.Domain.ValueObjects;

namespace Souq.Domain.Tests;

public class MoneyTests
{
    [Fact]
    public void مبلغ_سالب_يُرفض()
    {
        var act = () => new Money(-1);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void عملة_فارغة_تُرفض()
    {
        var act = () => new Money(10, "  ");

        act.Should().Throw<ArgumentException>();
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

        act.Should().Throw<InvalidOperationException>();
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

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Subtract_ناتج_سالب_يُرفض()
    {
        var act = () => new Money(5, "JOD").Subtract(new Money(10, "JOD"));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Multiply_يضرب_المبلغ_بالكمية()
    {
        var result = new Money(10).Multiply(3);

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
    public void مبلغان_متساويان_بنفس_القيمة_والعملة_يُعتبران_متساويين()
    {
        // Money سجل (record) — المساواة بالقيمة لا بالهوية.
        new Money(10, "JOD").Should().Be(new Money(10, "JOD"));
    }
}
