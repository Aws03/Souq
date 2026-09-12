using AwesomeAssertions;
using NSubstitute;
using Souq.Application.Common.Exceptions;
using Souq.Application.Common.Security;
using Souq.Application.Features.Wishlist;
using Souq.Application.Tests.TestDoubles;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Interfaces;

namespace Souq.Application.Tests.Wishlist;

// المفضّلة (المرحلة 13): العميل هو المتصل، والمنتج منشور في متجر السياق (غيره ⇒ 404 بلا كتابة)، والإضافة متساوية الأثر، والسقف
// 200، والدمج يحفظ ترتيب الزائر ويتجاهل بصمت ما لا يُعرف أو لا يتّسع. العرض بأسعار الكتالوج الحيّة يُثبَت في التكامل.
public class WishlistHandlersTests
{
    private readonly IWishlistRepository _wishlist = Substitute.For<IWishlistRepository>();
    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly IWishlistQueries _queries = Substitute.For<IWishlistQueries>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUser _me = TestCurrentUser.Customer(1);

    public WishlistHandlersTests()
    {
        _products.GetByIdAsync(5, Arg.Any<CancellationToken>()).Returns(TestCatalog.Product(id: 5));
        _queries.ListAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<WishlistItemDto>());
        _wishlist.ListForCustomerAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<WishlistItem>());
    }

    private static Product WithStatus(int id, ProductStatus status)
    {
        var product = TestCatalog.Product(id: id);
        product.ChangeStatus(status);
        return product;
    }

    private AddToWishlistHandler Add(ICurrentUser? user = null) =>
        new(_wishlist, _products, _queries, TestTenant.Context(), user ?? _me, _uow);

    private MergeWishlistHandler Merge() => new(_wishlist, _products, _queries, TestTenant.Context(), _me, _uow);

    [Fact]
    public async Task منتج_منشور_يُضاف_لمفضّلة_العميل_الحالي_وتعود_القائمة_بلغة_المتجر()
    {
        var result = await Add().Handle(new AddToWishlistCommand(5), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _wishlist.Received(1).AddAsync(Arg.Is<WishlistItem>(w => w.CustomerId == 1 && w.ProductId == 5), Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _queries.Received(1).ListAsync(1, "ar", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task المنتج_في_المفضّلة_أصلاً_نجاح_بلا_كتابة()
    {
        _wishlist.FindAsync(1, 5, Arg.Any<CancellationToken>()).Returns(new WishlistItem(1, 5));

        var result = await Add().Handle(new AddToWishlistCommand(5), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _wishlist.DidNotReceive().AddAsync(Arg.Any<WishlistItem>(), Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(ProductStatus.Draft)]
    [InlineData(ProductStatus.Archived)]
    [InlineData(null)]
    public async Task منتج_غير_منشور_أو_ليس_في_المتجر_404_بلا_كتابة(ProductStatus? status)
    {
        // null: منتج متجر آخر أو غير موجود — مرشّح المستأجر يعيده null من المستودع.
        _products.GetByIdAsync(6, Arg.Any<CancellationToken>()).Returns(status is { } s ? WithStatus(6, s) : null);

        var result = await Add().Handle(new AddToWishlistCommand(6), CancellationToken.None);

        result.ErrorCode.Should().Be("NotFound");
        await _wishlist.DidNotReceive().AddAsync(Arg.Any<WishlistItem>(), Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // R-07: الفئة المعطّلة تُخفي منتجها من المفضّلة كما من المتجر — إضافةً ودمجاً.
    [Fact]
    public async Task منتج_في_فئة_معطّلة_لا_يُضاف_للمفضّلة_ولا_يُدمَج()
    {
        _products.GetByIdAsync(7, Arg.Any<CancellationToken>()).Returns(TestCatalog.Product(id: 7, categoryActive: false));
        _products.GetManyAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>())
            .Returns(new List<Product> { TestCatalog.Product(id: 7, categoryActive: false) });

        (await Add().Handle(new AddToWishlistCommand(7), CancellationToken.None)).ErrorCode.Should().Be("NotFound");
        (await Merge().Handle(new MergeWishlistCommand([7]), CancellationToken.None)).IsSuccess.Should().BeTrue();

        await _wishlist.DidNotReceive().AddAsync(Arg.Any<WishlistItem>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task المفضّلة_الممتلئة_ترفض_الإضافة()
    {
        _wishlist.CountForCustomerAsync(1, Arg.Any<CancellationToken>()).Returns(WishlistItem.MaxItemsPerCustomer);

        var result = await Add().Handle(new AddToWishlistCommand(5), CancellationToken.None);

        result.ErrorCode.Should().Be("WishlistFull");
        await _wishlist.DidNotReceive().AddAsync(Arg.Any<WishlistItem>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task الحذف_يزيل_عنصر_العميل_الحالي_ومنتج_ليس_في_مفضّلته_404()
    {
        var item = new WishlistItem(1, 5);
        _wishlist.FindAsync(1, 5, Arg.Any<CancellationToken>()).Returns(item);
        var handler = new RemoveFromWishlistHandler(_wishlist, _queries, TestTenant.Context(), _me, _uow);

        (await handler.Handle(new RemoveFromWishlistCommand(5), CancellationToken.None)).IsSuccess.Should().BeTrue();
        _wishlist.Received(1).Remove(item);

        (await handler.Handle(new RemoveFromWishlistCommand(9), CancellationToken.None)).ErrorCode.Should().Be("NotFound");
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task الدمج_يضيف_المنشور_الجديد_بترتيب_الزائر_ويتجاهل_الموجود_والمخفي_والمجهول()
    {
        _wishlist.ListForCustomerAsync(1, Arg.Any<CancellationToken>()).Returns(new[] { new WishlistItem(1, 5) });
        // 404 (منتج متجر آخر أو محذوف) لا يعود من المستودع أصلاً.
        _products.GetManyAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>())
            .Returns(new List<Product> { TestCatalog.Product(id: 10), WithStatus(6, ProductStatus.Draft), TestCatalog.Product(id: 9) });
        var added = new List<int>();
        await _wishlist.AddAsync(Arg.Do<WishlistItem>(w => added.Add(w.ProductId)), Arg.Any<CancellationToken>());

        var result = await Merge().Handle(new MergeWishlistCommand([9, 5, 6, 404, 10, 9]), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        added.Should().Equal(9, 10);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task الدمج_لا_يتجاوز_السقف_ولا_يحفظ_إن_لم_يُضف_شيء()
    {
        _wishlist.ListForCustomerAsync(1, Arg.Any<CancellationToken>())
            .Returns(Enumerable.Range(1000, WishlistItem.MaxItemsPerCustomer - 1).Select(id => new WishlistItem(1, id)).ToList());
        _products.GetManyAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>())
            .Returns(new List<Product> { TestCatalog.Product(id: 9), TestCatalog.Product(id: 10) });
        var added = new List<int>();
        await _wishlist.AddAsync(Arg.Do<WishlistItem>(w => added.Add(w.ProductId)), Arg.Any<CancellationToken>());

        await Merge().Handle(new MergeWishlistCommand([9, 10]), CancellationToken.None);
        added.Should().Equal(9);

        _uow.ClearReceivedCalls();
        await Merge().Handle(new MergeWishlistCommand([1000]), CancellationToken.None);
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task الزائر_والموظّف_بلا_مفضّلة()
    {
        var guest = () => Add(TestCurrentUser.Anonymous()).Handle(new AddToWishlistCommand(5), CancellationToken.None);
        var staff = () => new GetWishlistHandler(_queries, TestTenant.Context(), TestCurrentUser.Staff())
            .Handle(new GetWishlistQuery(), CancellationToken.None);

        await guest.Should().ThrowAsync<AuthenticationRequiredException>();
        await staff.Should().ThrowAsync<CustomerAccountRequiredException>();
    }

    [Fact]
    public void الدمج_حتى_200_معرّف_موجب()
    {
        var validator = new MergeWishlistValidator();

        validator.Validate(new MergeWishlistCommand([1, 2])).IsValid.Should().BeTrue();
        validator.Validate(new MergeWishlistCommand([1, 0])).IsValid.Should().BeFalse();
        validator.Validate(new MergeWishlistCommand(Enumerable.Range(1, WishlistItem.MaxItemsPerCustomer + 1).ToList()))
            .IsValid.Should().BeFalse();
    }
}
