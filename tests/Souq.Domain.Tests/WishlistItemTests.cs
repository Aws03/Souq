using AwesomeAssertions;
using Souq.Domain.Entities;
using Souq.Domain.Exceptions;

namespace Souq.Domain.Tests;

// مفضّلة العميل (المرحلة 13): عنصر يخصّ عميلاً ومنتجاً محفوظَين، وسقف لكل عميل.
public class WishlistItemTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-3, 5)]
    public void عنصر_بلا_عميل_أو_منتج_محفوظ_يُرفض(int customerId, int productId)
    {
        var act = () => new WishlistItem(customerId, productId);
        act.Should().Throw<InvalidCustomerDataException>();
    }

    [Fact]
    public void السقف_مئتا_منتج_لكل_عميل()
    {
        WishlistItem.HasRoom(0).Should().BeTrue();
        WishlistItem.HasRoom(WishlistItem.MaxItemsPerCustomer - 1).Should().BeTrue();
        WishlistItem.HasRoom(WishlistItem.MaxItemsPerCustomer).Should().BeFalse();
    }
}
