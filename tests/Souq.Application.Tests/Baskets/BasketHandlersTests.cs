using AwesomeAssertions;
using NSubstitute;
using Souq.Application.Common.Security;
using Souq.Application.Features.Baskets;
using Souq.Application.Features.Baskets.Contracts;
using Souq.Application.Features.Inventory.Contracts;
using Souq.Application.Tests.TestDoubles;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Tests.Baskets;

// سلة المتصل (المرحلة 8): زائر برمز جديد تُحفظ بصمته وحدها، عميل بسلته، الدمج عند الدخول، الرمز البائد، والمتاح قبل
// الإضافة. الحساب نفسه (الأسعار والخصم) في PricingServiceTests — هنا تسعير بديل بسيط.
public class BasketHandlersTests
{
    private readonly IBasketRepository _baskets = Substitute.For<IBasketRepository>();
    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly IStockAvailability _availability = Substitute.For<IStockAvailability>();
    private readonly IPricing _pricing = Substitute.For<IPricing>();
    private readonly IUnitOfWork _uow = TestUnitOfWork.Create();
    private readonly FixedClock _clock = new();
    private readonly BasketSettings _settings = new();
    private Basket? _added;

    public BasketHandlersTests()
    {
        _baskets.When(b => b.AddAsync(Arg.Any<Basket>(), Arg.Any<CancellationToken>())).Do(call => _added = call.Arg<Basket>());
        _availability.AvailableAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<IReadOnlyCollection<int>>().ToDictionary(id => id, _ => 10));
        _pricing.QuoteAsync(Arg.Any<IReadOnlyList<PricingLine>>(), Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<ShippingRequest?>(),
                Arg.Any<CancellationToken>())
            .Returns(call => Quote(call.Arg<IReadOnlyList<PricingLine>>()));
    }

    private BasketResolver Resolver(ICurrentUser user) => new(_baskets, user, _settings, _clock);
    // كاتبٌ حقيقي على نفس المستودع المزيّف: الإعادة سلوكٌ يُختبَر لا يُتخطّى (BasketWriterTests تُثبّت آليّته).
    private BasketWriter Writer() => new(_baskets);
    private BasketViews Views(ICurrentUser user) => new(_pricing, _availability, user);
    private AddBasketItemHandler AddHandler(ICurrentUser user) => new(_products, _availability, Resolver(user), Views(user), _uow, Writer());
    private GetBasketHandler GetHandler(ICurrentUser user) => new(Resolver(user), Views(user), _uow, Writer());
    private BasketLines Lines(ICurrentUser user) => new(_availability, Resolver(user), Views(user), _uow, Writer());
    private SetBasketItemQuantityHandler SetHandler(ICurrentUser user) => new(Lines(user));
    private SetBasketLineQuantityHandler SetLineHandler(ICurrentUser user) => new(Lines(user));
    private RemoveBasketItemHandler RemoveHandler(ICurrentUser user) => new(Lines(user));
    private RemoveBasketLineHandler RemoveLineHandler(ICurrentUser user) => new(Lines(user));

    private DateTime Tomorrow => _clock.UtcNow.AddDays(1);

    // كل سطر قابل للبيع بعشرة دنانير.
    private static PriceQuote Quote(IReadOnlyList<PricingLine> lines)
    {
        var priced = lines.Select(l => new PricedLine(
            l.ProductId, l.VariantId ?? l.ProductId, "صنف", new Dictionary<string, string> { ["ar"] = "صنف" }, null,
            new Money(10, "JOD"), l.Quantity, new Money(10 * l.Quantity, "JOD"), Sellable: true)).ToList();
        var total = new Money(priced.Sum(l => l.LineTotal.Amount), "JOD");
        return new PriceQuote("JOD", priced, total, null, Money.Zero("JOD"), Money.Zero("JOD"), Money.Zero("JOD"), total);
    }

    private void ProductExists(int id) =>
        _products.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(TestCatalog.Product(price: 10, id: id));

    [Fact]
    public async Task زائر_بلا_رمز_يُصدَر_له_رمز_جديد_وتُحفظ_بصمته_وحدها()
    {
        ProductExists(5);

        var result = await AddHandler(TestCurrentUser.Anonymous()).Handle(new AddBasketItemCommand(null, 5, 2), CancellationToken.None);

        var value = result.Value!;
        value.Cookie.Should().Be(GuestCookieAction.Set);
        GuestBasketTokens.IsWellFormed(value.GuestToken).Should().BeTrue();
        _added!.GuestTokenHash.Should().Be(GuestBasketTokens.Hash(value.GuestToken!)).And.NotBe(value.GuestToken);
        _added.ExpiresAt.Should().Be(_clock.UtcNow.AddDays(_settings.GuestLifetimeDays));
        value.GuestExpiresAt.Should().Be(_added.ExpiresAt);
        value.Basket.Lines.Should().ContainSingle().Which.Quantity.Should().Be(2);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task منتج_غير_متاح_404_وتجاوز_المتاح_يُرفض_بلا_سلة_ولا_حفظ()
    {
        ProductExists(5);
        _availability.AvailableAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<int, int> { [5] = 1 });
        var handler = AddHandler(TestCurrentUser.Anonymous());

        (await handler.Handle(new AddBasketItemCommand(null, 7, 1), CancellationToken.None)).ErrorCode.Should().Be("NotFound");
        (await handler.Handle(new AddBasketItemCommand(null, 5, 2), CancellationToken.None)).ErrorCode.Should().Be("InsufficientStock");

        _added.Should().BeNull();
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // R-07: المنتج في فئة معطّلة مخفيّ من المتجر — ولا يُضاف للسلة بمعرّفه أيضاً.
    [Fact]
    public async Task منتج_في_فئة_معطّلة_لا_يُضاف_للسلة()
    {
        _products.GetByIdAsync(8, Arg.Any<CancellationToken>())
            .Returns(TestCatalog.Product(price: 10, id: 8, categoryActive: false));

        var result = await AddHandler(TestCurrentUser.Anonymous())
            .Handle(new AddBasketItemCommand(null, 8, 1), CancellationToken.None);

        result.ErrorCode.Should().Be("NotFound");
        _added.Should().BeNull();
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task دخول_العميل_يدمج_سلة_الزائر_ويحذفها_ويمسح_الرمز()
    {
        var token = GuestBasketTokens.New();
        var guest = Basket.ForGuest(GuestBasketTokens.Hash(token), Tomorrow);
        guest.Add(5, 5, 1, Tomorrow);
        guest.Add(6, 6, 2, Tomorrow);
        var mine = Basket.ForCustomer(1, Tomorrow);
        mine.Add(5, 5, 3, Tomorrow);
        _baskets.GetForGuestAsync(GuestBasketTokens.Hash(token), Arg.Any<CancellationToken>()).Returns(guest);
        _baskets.GetForCustomerAsync(1, Arg.Any<CancellationToken>()).Returns(mine);

        var result = await GetHandler(TestCurrentUser.Customer(1)).Handle(new GetBasketQuery(token), CancellationToken.None);

        result.Value!.Cookie.Should().Be(GuestCookieAction.Clear);
        result.Value.Basket.Lines.Select(l => (l.ProductId, l.Quantity)).Should().BeEquivalentTo(new[] { (5, 4), (6, 2) });
        _baskets.Received(1).Remove(guest);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task عميل_بلا_سلة_يستقبل_سلة_الزائر_في_سلة_جديدة()
    {
        var token = GuestBasketTokens.New();
        var guest = Basket.ForGuest(GuestBasketTokens.Hash(token), Tomorrow);
        guest.Add(5, 5, 1, Tomorrow);
        _baskets.GetForGuestAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(guest);

        var result = await GetHandler(TestCurrentUser.Customer(1)).Handle(new GetBasketQuery(token), CancellationToken.None);

        _added!.CustomerId.Should().Be(1);
        _added.Lines.Should().ContainSingle().Which.ProductId.Should().Be(5);
        result.Value!.Basket.ItemCount.Should().Be(1);
    }

    [Fact]
    public async Task الرمز_المنتهي_أو_المشوّه_يُعامل_كغيابه_ويُمسح()
    {
        var expired = Basket.ForGuest(new string('b', Basket.GuestTokenHashLength), _clock.UtcNow.AddMinutes(-1));
        _baskets.GetForGuestAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(expired);
        var handler = GetHandler(TestCurrentUser.Anonymous());

        var result = await handler.Handle(new GetBasketQuery(GuestBasketTokens.New()), CancellationToken.None);
        var malformed = await handler.Handle(new GetBasketQuery("not-a-token"), CancellationToken.None);

        (result.Value!.Cookie, result.Value.Basket.Lines.Count).Should().Be((GuestCookieAction.Clear, 0));
        _baskets.Received(1).Remove(expired);
        malformed.Value!.Cookie.Should().Be(GuestCookieAction.Clear);
        // الرمز المشوّه لا يُبحث عنه أصلاً: بحث واحد فقط (للرمز السليم).
        await _baskets.Received(1).GetForGuestAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task الموظّف_بلا_ملف_شراء_يستعمل_سلة_زائر()
    {
        ProductExists(5);

        var result = await AddHandler(TestCurrentUser.Staff()).Handle(new AddBasketItemCommand(null, 5), CancellationToken.None);

        (result.Value!.Cookie, _added!.IsGuest).Should().Be((GuestCookieAction.Set, true));
    }

    [Fact]
    public async Task تعديل_صنف_ليس_في_السلة_404_والزيادة_وحدها_تُقاس_بالمتاح()
    {
        var mine = Basket.ForCustomer(1, Tomorrow);
        mine.Add(5, 5, 3, Tomorrow);
        _baskets.GetForCustomerAsync(1, Arg.Any<CancellationToken>()).Returns(mine);
        _availability.AvailableAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<int, int> { [5] = 3 });
        var handler = SetHandler(TestCurrentUser.Customer(1));

        (await handler.Handle(new SetBasketItemQuantityCommand(null, 9, 1), CancellationToken.None)).ErrorCode.Should().Be("NotFound");
        (await handler.Handle(new SetBasketItemQuantityCommand(null, 5, 4), CancellationToken.None)).ErrorCode.Should().Be("InsufficientStock");
        (await handler.Handle(new SetBasketItemQuantityCommand(null, 5, 1), CancellationToken.None)).IsSuccess.Should().BeTrue();

        mine.Lines.Single().Quantity.Should().Be(1);
        mine.ExpiresAt.Should().Be(_clock.UtcNow.AddDays(_settings.CustomerLifetimeDays));
    }

    [Fact]
    public async Task المنسّق_يحذف_ما_أعاده_المستودع_منتهياً()
    {
        var old = Basket.ForCustomer(1, _clock.UtcNow.AddDays(-1));
        _baskets.ListExpiredAsync(_clock.UtcNow, 500, Arg.Any<CancellationToken>()).Returns(new List<Basket> { old });

        var purged = await new PurgeExpiredBasketsHandler(_baskets, _uow, _clock)
            .Handle(new PurgeExpiredBasketsCommand(), CancellationToken.None);

        purged.Should().Be(1);
        _baskets.Received(1).Remove(old);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── المتغيّرات (ProductVariants.md، V1) ── قميص (منتج 5) بمتغيّرين: الافتراضي 5 والثاني 51.
    private Product ShirtExists()
    {
        var shirt = TestCatalog.Product(price: 20, id: 5);
        TestCatalog.WithId(TestCatalog.AddVariant(shirt, new Money(25, "JOD")), 51);
        _products.GetByIdAsync(5, Arg.Any<CancellationToken>()).Returns(shirt);
        return shirt;
    }

    private Basket CustomerBasket()
    {
        var mine = Basket.ForCustomer(1, Tomorrow);
        _baskets.GetForCustomerAsync(1, Arg.Any<CancellationToken>()).Returns(mine);
        return mine;
    }

    [Fact]
    public async Task متغيّران_من_المنتج_نفسه_سطران_منفصلان_في_السلة()
    {
        ShirtExists();
        var mine = CustomerBasket();
        var handler = AddHandler(TestCurrentUser.Customer(1));

        (await handler.Handle(new AddBasketItemCommand(null, 5, 1, VariantId: 5), CancellationToken.None)).IsSuccess.Should().BeTrue();
        var result = await handler.Handle(new AddBasketItemCommand(null, 5, 2, VariantId: 51), CancellationToken.None);
        await handler.Handle(new AddBasketItemCommand(null, 5, 1, VariantId: 51), CancellationToken.None);

        mine.Lines.Select(l => (l.ProductId, l.VariantId, l.Quantity)).Should().Equal((5, 5, 1), (5, 51, 3));
        result.Value!.Basket.Lines.Select(l => l.VariantId).Should().Equal(5, 51);
    }

    [Fact]
    public async Task بلا_متغيّر_لمنتج_بأكثر_من_متغيّر_نشط_يُطلب_التحديد_والغريب_والمعطّل_404()
    {
        var shirt = ShirtExists();
        var charger = TestCatalog.Product(price: 3, id: 6);
        _products.GetByIdAsync(6, Arg.Any<CancellationToken>()).Returns(charger);
        var handler = AddHandler(TestCurrentUser.Anonymous());

        (await handler.Handle(new AddBasketItemCommand(null, 5, 1), CancellationToken.None)).ErrorCode.Should().Be("VariantRequired");
        // متغيّر منتج آخر من المتجر نفسه لا يُضاف مع هذا المنتج (ولا مع ذاك).
        (await handler.Handle(new AddBasketItemCommand(null, 6, 1, VariantId: 51), CancellationToken.None)).ErrorCode.Should().Be("NotFound");
        (await handler.Handle(new AddBasketItemCommand(null, 5, 1, VariantId: 6), CancellationToken.None)).ErrorCode.Should().Be("NotFound");
        shirt.DeactivateVariant(51);
        (await handler.Handle(new AddBasketItemCommand(null, 5, 1, VariantId: 51), CancellationToken.None)).ErrorCode.Should().Be("NotFound");

        _added.Should().BeNull();
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());

        // بعد تعطيل الثاني عاد للقميص متغيّر نشط واحد: الإضافة بلا متغيّر تعمل كما لمنتج بسيط.
        (await handler.Handle(new AddBasketItemCommand(null, 5, 1), CancellationToken.None)).IsSuccess.Should().BeTrue();
        _added!.Lines.Should().ContainSingle().Which.VariantId.Should().Be(5);
    }

    [Fact]
    public async Task مسار_المنتج_ملتبس_حين_له_سطران_ومسار_المتغيّر_يصيب_سطره_وحده()
    {
        var mine = CustomerBasket();
        mine.Add(5, 5, 2, Tomorrow);
        mine.Add(5, 51, 3, Tomorrow);
        var user = TestCurrentUser.Customer(1);

        (await SetHandler(user).Handle(new SetBasketItemQuantityCommand(null, 5, 1), CancellationToken.None))
            .ErrorCode.Should().Be("VariantRequired");
        (await RemoveHandler(user).Handle(new RemoveBasketItemCommand(null, 5), CancellationToken.None))
            .ErrorCode.Should().Be("VariantRequired");
        mine.Lines.Select(l => l.Quantity).Should().Equal(2, 3);

        (await SetLineHandler(user).Handle(new SetBasketLineQuantityCommand(null, 51, 1), CancellationToken.None)).IsSuccess.Should().BeTrue();
        (await RemoveLineHandler(user).Handle(new RemoveBasketLineCommand(null, 5), CancellationToken.None)).IsSuccess.Should().BeTrue();
        (await SetLineHandler(user).Handle(new SetBasketLineQuantityCommand(null, 99, 1), CancellationToken.None))
            .ErrorCode.Should().Be("NotFound");

        mine.Lines.Select(l => (l.VariantId, l.Quantity)).Should().Equal((51, 1));

        // بقي للمنتج سطر واحد: مسار المنتج صالح من جديد.
        (await RemoveHandler(user).Handle(new RemoveBasketItemCommand(null, 5), CancellationToken.None)).IsSuccess.Should().BeTrue();
        mine.Lines.Should().BeEmpty();
    }

    [Fact]
    public void معرّف_المتغيّر_اختياري_وإن_أُرسل_فموجب()
    {
        new AddBasketItemValidator().Validate(new AddBasketItemCommand(null, 5, 1)).IsValid.Should().BeTrue();
        new AddBasketItemValidator().Validate(new AddBasketItemCommand(null, 5, 1, VariantId: 0)).IsValid.Should().BeFalse();
        new SetBasketLineQuantityValidator().Validate(new SetBasketLineQuantityCommand(null, 0, 1)).IsValid.Should().BeFalse();
        new SetBasketLineQuantityValidator().Validate(new SetBasketLineQuantityCommand(null, 51, Basket.MaxQuantityPerLine + 1)).IsValid.Should().BeFalse();
    }
}
