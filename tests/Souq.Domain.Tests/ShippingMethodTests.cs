using AwesomeAssertions;
using Souq.Domain.Entities;
using Souq.Domain.Exceptions;
using Souq.Domain.ValueObjects;

namespace Souq.Domain.Tests;

// ============================================================================
// طريقة الشحن (المرحلة 12، ADR-0032): سعر ثابت ومجانية حين يبلغ الإجمالي الحدّ، دول موحَّدة (بلا دول ⇒ كل مكان، وبدول ⇒ عنوان
// بدولة معروفة منها)، مدّة تقديرية منطقية، ورابط تتبّع https بعلامة الرقم — وتعديل مرفوض لا يغيّر شيئاً.
// ============================================================================
public class ShippingMethodTests
{
    private static ShippingMethod Method(decimal price = 2.5m, decimal? freeOver = null, string[]? countries = null,
        string? template = null, int? minDays = 1, int? maxDays = 3) =>
        new("توصيل", new Money(price, "JOD"), freeOver, "Aramex", template, minDays, maxDays, countries);

    [Fact]
    public void السعر_ثابت_ومجاني_حين_يبلغ_الإجمالي_الحدّ()
    {
        var method = Method(2.5m, freeOver: 50m);

        method.RateFor(new Money(49.999m, "JOD")).Should().Be(new Money(2.5m, "JOD"));
        method.RateFor(new Money(50m, "JOD")).Should().Be(Money.Zero("JOD"));
        method.FreeOver.Should().Be(new Money(50m, "JOD"));
        method.Invoking(m => m.RateFor(new Money(60, "USD"))).Should().Throw<InvalidShippingMethodException>();
    }

    [Fact]
    public void الدول_تُوحَّد_وبلا_دول_تخدم_كل_مكان_وبدول_تلزم_دولة_معروفة()
    {
        Method().Serves(null).Should().BeTrue();

        var gulfAndJordan = Method(countries: [" jo ", "SA", "jo", ""]);

        (gulfAndJordan.Countries, gulfAndJordan.CountryList.Count).Should().Be(("JO,SA", 2));
        (gulfAndJordan.Serves("JO"), gulfAndJordan.Serves("jo"), gulfAndJordan.Serves("EG"), gulfAndJordan.Serves(null))
            .Should().Be((true, true, false, false));
    }

    [Fact]
    public void رابط_التتبّع_يُبنى_من_القالب_والرقم_مرمَّزاً()
    {
        var method = Method(template: " https://track.example/?n={number} ");

        method.TrackingUrlTemplate.Should().Be("https://track.example/?n={number}");
        ShippingMethod.TrackingUrl(method.TrackingUrlTemplate, "AB 12/3").Should().Be("https://track.example/?n=AB%2012%2F3");
        ShippingMethod.TrackingUrl(method.TrackingUrlTemplate, null).Should().BeNull();
        ShippingMethod.TrackingUrl(null, "AB12").Should().BeNull();
    }

    public static TheoryData<string, Func<ShippingMethod>> Invalid => new()
    {
        { "اسم فارغ", () => new ShippingMethod(" ", new Money(1, "JOD"), null, null, null, null, null, null) },
        { "اسم طويل", () => new ShippingMethod(new string('س', 101), new Money(1, "JOD"), null, null, null, null, null, null) },
        { "حدّ مجانية صفر", () => Method(freeOver: 0) },
        { "أقلّ بلا أكثر", () => Method(minDays: 2, maxDays: null) },
        { "أقلّ أكبر من أكثر", () => Method(minDays: 5, maxDays: 2) },
        { "مدّة فوق الحدّ", () => Method(minDays: 1, maxDays: 91) },
        { "رابط بلا https", () => Method(template: "http://track.example/{number}") },
        { "رابط بلا علامة الرقم", () => Method(template: "https://track.example/track") },
        { "دولة بثلاثة أحرف", () => Method(countries: ["JOR"]) },
        { "دولة بأرقام", () => Method(countries: ["1A"]) },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public void القواعد_تُحرس_عند_الإنشاء(string _, Func<ShippingMethod> create) =>
        create.Should().Throw<InvalidShippingMethodException>();

    [Fact]
    public void حدّ_المجانية_بخانات_عملة_السعر_وتعديل_مرفوض_لا_يغيّر_شيئاً()
    {
        var tooPrecise = () => Method(freeOver: 1.2345m);
        tooPrecise.Should().Throw<DomainException>("الدينار ثلاث خانات");

        var method = Method();
        method.Invoking(m => m.Update("جديد", new Money(9, "JOD"), null, null, "https://x.example/nope", null, null, null, 0))
            .Should().Throw<InvalidShippingMethodException>();
        (method.Name, method.Price.Amount).Should().Be(("توصيل", 2.5m));

        method.Deactivate();
        method.IsActive.Should().BeFalse();
    }
}
