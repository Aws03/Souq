using AwesomeAssertions;
using Souq.Domain.Exceptions;

namespace Souq.Domain.Tests;

// الرمز (Code) عقد ثابت تترجمه الواجهة (ADR-0017): تغييره كسر للعقد، فنثبّته هنا.
public class DomainExceptionCodeTests
{
    public static TheoryData<DomainException, string> Cases => new()
    {
        { new InsufficientStockException("سماعات", 3, 1), "InsufficientStock" },
        { new InvalidOrderOperationException("x"), "InvalidOrderOperation" },
        { new InvalidProductDataException("x"), "InvalidProductData" },
        { new InvalidCategoryException("x"), "InvalidCategory" },
        { new InvalidCategoryParentException("x"), "InvalidParent" },
        { new InvalidInventoryOperationException("x"), "InvalidInventoryOperation" },
        { new InvalidCustomerDataException("x"), "InvalidCustomerData" },
        { new InvalidBasketOperationException("x"), "InvalidBasketOperation" },
        { new InvalidCouponException("x"), "InvalidCoupon" },
        { new InvalidReviewException("x"), "InvalidReview" },
        { new InvalidPasswordResetException("x"), "ResetTokenExpired" },
        { new InvalidMoneyException("x"), "InvalidMoney" },
        { new InvalidTenantOperationException("x"), "InvalidTenantOperation" },
        { new InvalidIdentityOperationException("x"), "InvalidIdentityOperation" },
        { new InvalidEmailVerificationException("x"), "VerificationTokenExpired" },
        { new InvalidPaymentOperationException("x"), "InvalidPaymentOperation" },
        { new InvalidPaymentOperationException("x", "RefundExceedsPayment"), "RefundExceedsPayment" },
        { new InvalidShippingMethodException("x"), "InvalidShippingMethod" },
        { new InvalidNotificationException("x"), "InvalidNotification" },
        { new InvalidProductVariantException("DuplicateVariantCombination", "x"), "DuplicateVariantCombination" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void كل_استثناء_مجال_يحمل_رمزاً_ثابتاً(DomainException exception, string expectedCode)
    {
        exception.Code.Should().Be(expectedCode);
        exception.Message.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void كل_أنواع_استثناءات_المجال_مغطّاة_بالاختبار()
    {
        // حارس: نوع جديد بلا حالة هنا يُفشل الاختبار فيُضاف رمزه للعقد صراحةً.
        var tested = Cases.Select(c => ((DomainException)c[0]).GetType()).ToHashSet();
        var all = typeof(DomainException).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(DomainException)) && !t.IsAbstract);

        all.Should().BeSubsetOf(tested);
    }
}
