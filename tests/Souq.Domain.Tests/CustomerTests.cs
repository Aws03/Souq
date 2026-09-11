using AwesomeAssertions;
using Souq.Domain.Entities;
using Souq.Domain.Exceptions;

namespace Souq.Domain.Tests;

public class CustomerTests
{
    private static Customer NewCustomer() =>
        new("مستخدم", "user@souq.com", "hashed");

    [Fact]
    public void GenerateResetToken_يُعيد_رمزاً_غير_فارغ_ويُعيّن_انتهاءً_بعد_ساعتين_تقريباً()
    {
        var customer = NewCustomer();

        var token = customer.GenerateResetToken();

        token.Should().NotBeNullOrWhiteSpace();
        customer.PasswordResetToken.Should().Be(token);
        customer.PasswordResetTokenExpiry.Should().BeCloseTo(DateTime.UtcNow.AddHours(2), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void ResetPassword_برمز_صالح_يُحدّث_التجزئة_ويمسح_حقول_الرمز()
    {
        var customer = NewCustomer();
        customer.GenerateResetToken();

        customer.ResetPassword("new-hashed-value");

        customer.PasswordHash.Should().Be("new-hashed-value");
        customer.PasswordResetToken.Should().BeNull();
        customer.PasswordResetTokenExpiry.Should().BeNull();
    }

    [Fact]
    public void ResetPassword_برمز_منتهي_الصلاحية_يرمي_ولا_يغيّر_كلمة_المرور()
    {
        var customer = NewCustomer();
        customer.GenerateResetToken();
        // نحاكي انتهاء الصلاحية مباشرة عبر Reflection (لا باب عام لضبط تاريخ
        // ماضٍ — نفس أسلوب محاكاة العلاقات في ProductHandlersTests).
        typeof(Customer).GetProperty(nameof(Customer.PasswordResetTokenExpiry))!
            .SetValue(customer, DateTime.UtcNow.AddMinutes(-1));

        var act = () => customer.ResetPassword("new-hashed-value");

        act.Should().Throw<InvalidPasswordResetException>();
        customer.PasswordHash.Should().Be("hashed"); // لم يتغيّر
    }

    [Fact]
    public void ResetPassword_بلا_طلب_إعادة_تعيين_قائم_يرمي()
    {
        var customer = NewCustomer(); // لم يُستدعَ GenerateResetToken إطلاقاً

        var act = () => customer.ResetPassword("new-hashed-value");

        act.Should().Throw<InvalidPasswordResetException>();
    }
}
