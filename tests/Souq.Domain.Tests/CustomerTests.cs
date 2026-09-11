using AwesomeAssertions;
using Souq.Domain.Entities;
using Souq.Domain.Exceptions;

namespace Souq.Domain.Tests;

public class CustomerTests
{
    private static Customer NewCustomer() =>
        new("مستخدم", "user@souq.com", "hashed");

    [Fact]
    public void GenerateResetToken_يُخزّن_التجزئة_فقط_ويُعيد_الرمز_الخام_مرة_واحدة()
    {
        var customer = NewCustomer();

        var token = customer.GenerateResetToken();

        token.Should().NotBeNullOrWhiteSpace();
        // القاعدة لا تحمل الرمز نفسه أبداً — من يقرؤها لا يملك رابطاً صالحاً (Phase 0 B6).
        customer.PasswordResetTokenHash.Should().Be(Customer.HashResetToken(token));
        customer.PasswordResetTokenHash.Should().NotBe(token);
        customer.PasswordResetTokenExpiry.Should().BeCloseTo(DateTime.UtcNow.AddHours(2), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void GenerateResetToken_يولّد_رمزاً_عشوائياً_بطول_256_بت()
    {
        var customer = NewCustomer();

        var first = customer.GenerateResetToken();
        var second = customer.GenerateResetToken();

        first.Should().NotBe(second);
        first.Should().HaveLength(43);             // 32 بايت بترميز base64url بلا حشو
        first.Should().MatchRegex("^[A-Za-z0-9_-]+$"); // آمن داخل رابط بلا ترميز إضافي
    }

    [Fact]
    public void HashResetToken_حتمية_وتُنتج_64_حرفاً_سداسياً()
    {
        var hash = Customer.HashResetToken("sample-token");

        hash.Should().Be(Customer.HashResetToken("sample-token"));
        hash.Should().MatchRegex("^[0-9A-F]{64}$");
    }

    [Fact]
    public void ResetPassword_برمز_صالح_يُحدّث_التجزئة_ويمسح_حقول_الرمز()
    {
        var customer = NewCustomer();
        customer.GenerateResetToken();

        customer.ResetPassword("new-hashed-value");

        customer.PasswordHash.Should().Be("new-hashed-value");
        customer.PasswordResetTokenHash.Should().BeNull();
        customer.PasswordResetTokenExpiry.Should().BeNull();
    }

    [Fact]
    public void ResetPassword_برمز_منتهي_الصلاحية_يرمي_ولا_يغيّر_كلمة_المرور()
    {
        var customer = NewCustomer();
        customer.GenerateResetToken();
        // نحاكي انتهاء الصلاحية مباشرة عبر Reflection (لا باب عام لضبط تاريخ ماضٍ).
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
