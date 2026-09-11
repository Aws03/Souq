using AwesomeAssertions;
using NSubstitute;
using Souq.Application.Features.Shipping;
using Souq.Application.Tests.TestDoubles;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Tests.Shipping;

// ============================================================================
// المزوّد الافتراضي لأسعار الشحن (المرحلة 12): طرق المتجر المفعّلة التي تخدم الدولة، بسعرها لإجمالي السلة (المجانية فوق
// الحدّ)، بترتيب المدير ثم الأرخص؛ عنوان بلا دولة ⇒ غير المقيَّدة وحدها؛ طريقة بعملة قديمة لا تُعرض؛ ومتجر بلا طرق لا يشحن.
// ============================================================================
public class StoreShippingRatesTests
{
    private readonly IShippingMethodRepository _methods = Substitute.For<IShippingMethodRepository>();

    private void Active(params ShippingMethod[] methods) =>
        _methods.ListAsync(true, Arg.Any<CancellationToken>()).Returns(methods);

    private Task<Souq.Application.Features.Shipping.Contracts.ShippingQuote> Quote(decimal goods, string? country) =>
        new StoreShippingRates(_methods).QuoteAsync(new Money(goods, "JOD"), country, CancellationToken.None);

    [Fact]
    public async Task الطرق_التي_تخدم_الدولة_بسعرها_وبترتيب_المدير_ثم_الأرخص()
    {
        var local = TestCatalog.WithId(
            new ShippingMethod("محلي", new Money(2, "JOD"), 30, null, null, 1, 2, ["JO"], sortOrder: 1), 1);
        var express = TestCatalog.WithId(
            new ShippingMethod("سريع", new Money(5, "JOD"), null, "DHL", null, 1, 1, null, sortOrder: 0), 2);
        var gulf = TestCatalog.WithId(
            new ShippingMethod("الخليج", new Money(9, "JOD"), null, null, null, 3, 6, ["SA", "AE"], sortOrder: 0), 3);
        var oldCurrency = TestCatalog.WithId(
            new ShippingMethod("قديمة", new Money(1, "USD"), null, null, null, null, null, null), 4);
        Active(local, express, gulf, oldCurrency);

        var jordan = await Quote(30, "JO");
        jordan.Options.Select(o => (o.MethodId, o.Cost.Amount)).Should().Equal((2, 5m), (1, 0m));
        jordan.Options[0].Should().BeEquivalentTo(new { Name = "سريع", Carrier = "DHL", MinDays = 1, MaxDays = 1 },
            o => o.ExcludingMissingMembers());
        jordan.StoreShips.Should().BeTrue();

        (await Quote(10, "JO")).Options.Select(o => (o.MethodId, o.Cost.Amount)).Should().Equal((2, 5m), (1, 2m));
        (await Quote(10, "SA")).Options.Select(o => o.MethodId).Should().Equal(2, 3);
        (await Quote(10, null)).Options.Select(o => o.MethodId).Should().Equal(2);
        (await Quote(10, "EG")).Options.Select(o => o.MethodId).Should().Equal(2);
    }

    [Fact]
    public async Task متجر_بلا_طرق_مفعّلة_لا_يشحن_ومتجر_بطرق_لا_تخدم_العنوان_يشحن_بلا_خيارات()
    {
        Active();
        var none = await Quote(10, "JO");
        (none.Options.Count, none.StoreShips).Should().Be((0, false));

        Active(new ShippingMethod("الخليج", new Money(9, "JOD"), null, null, null, null, null, ["SA"]));
        var elsewhere = await Quote(10, "JO");
        (elsewhere.Options.Count, elsewhere.StoreShips).Should().Be((0, true));
    }
}
