using NSubstitute;
using Souq.Application.Features.Shipping.Contracts;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Tests.TestDoubles;

// أسعار شحن للاختبار (المرحلة 12). None: متجر بلا طرق شحن — شحن مجاني بلا اختيار، كما قبل المرحلة. Methods: متجر بطرق
// محدَّدة تُعرض لأي عنوان (تصفية الدول مسؤولية StoreShippingRates ولها اختباراتها).
public static class TestShipping
{
    public static IShippingRateProvider None() => Methods();

    public static IShippingRateProvider Methods(params ShippingOption[] options)
    {
        var rates = Substitute.For<IShippingRateProvider>();
        rates.QuoteAsync(Arg.Any<Money>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new ShippingQuote(options, StoreShips: options.Length > 0));
        return rates;
    }

    public static ShippingOption Option(int id, decimal cost, string currency = "JOD", string name = "توصيل") =>
        new(id, name, new Money(cost, currency), "Aramex", "https://track.example/{number}", 1, 3);
}
