using AwesomeAssertions;
using NSubstitute;
using Souq.Application.Features.Shipping;
using Souq.Application.Tests.TestDoubles;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Tests.Shipping;

// طرق الشحن من الإدارة (المرحلة 12): السعر بعملة المتجر دائماً، التعديل يطبّق كل الحقول والتفعيل، وطريقة غير موجودة في
// المتجر 404 تعديلاً وحذفاً.
public class ShippingMethodHandlersTests
{
    private readonly IShippingMethodRepository _methods = Substitute.For<IShippingMethodRepository>();
    private readonly IUnitOfWork _uow = TestUnitOfWork.Create();

    [Fact]
    public async Task الإنشاء_بعملة_المتجر_ومعطّلة_حين_تُطلب_كذلك()
    {
        ShippingMethod? added = null;
        _methods.When(m => m.AddAsync(Arg.Any<ShippingMethod>(), Arg.Any<CancellationToken>())).Do(call => added = call.Arg<ShippingMethod>());

        var result = await new CreateShippingMethodHandler(_methods, TestTenant.Context("KWD"), _uow).Handle(
            new CreateShippingMethodCommand(new ShippingMethodInput("توصيل", 1.5m, Countries: ["kw"], IsActive: false)),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        (added!.Price, added.Countries, added.IsActive).Should().Be((new Money(1.5m, "KWD"), "KW", false));
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task التعديل_يطبّق_كل_الحقول_والتفعيل()
    {
        var method = new ShippingMethod("قديم", new Money(1, "JOD"), null, null, null, null, null, null);
        _methods.GetByIdAsync(3, Arg.Any<CancellationToken>()).Returns(method);

        await new UpdateShippingMethodHandler(_methods, TestTenant.Context(), _uow).Handle(new UpdateShippingMethodCommand(3,
            new ShippingMethodInput("جديد", 2.5m, 40m, "DHL", "https://track.example/{number}", 1, 2, ["JO"], IsActive: false, SortOrder: 3)),
            CancellationToken.None);

        (method.Name, method.Price.Amount, method.FreeOverAmount, method.Carrier, method.IsActive, method.SortOrder, method.Countries)
            .Should().Be(("جديد", 2.5m, (decimal?)40m, "DHL", false, 3, "JO"));
    }

    [Fact]
    public async Task طريقة_غير_موجودة_في_المتجر_404_تعديلاً_وحذفاً()
    {
        _methods.GetByIdAsync(5, Arg.Any<CancellationToken>()).Returns((ShippingMethod?)null);

        (await new UpdateShippingMethodHandler(_methods, TestTenant.Context(), _uow)
            .Handle(new UpdateShippingMethodCommand(5, new ShippingMethodInput("x", 1)), CancellationToken.None)).ErrorCode.Should().Be("NotFound");
        (await new DeleteShippingMethodHandler(_methods, _uow)
            .Handle(new DeleteShippingMethodCommand(5), CancellationToken.None)).ErrorCode.Should().Be("NotFound");
        _methods.DidNotReceiveWithAnyArgs().Remove(default!);
    }
}
