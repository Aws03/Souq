using AwesomeAssertions;
using Souq.Domain.Entities;
using Souq.Domain.Exceptions;

namespace Souq.Domain.Tests;

public class CustomerTests
{
    // الوقت يُمرَّر صراحةً (Phase 0 D12): لا DateTime.UtcNow داخل الكيان، ولا Reflection هنا.
    private static readonly DateTime Now = new(2026, 9, 11, 10, 0, 0, DateTimeKind.Utc);

    private static Customer NewCustomer() =>
        new("مستخدم", "user@souq.com", "hashed");

    [Fact]
    public void GenerateResetToken_يُخزّن_التجزئة_فقط_ويُعيد_الرمز_الخام_مرة_واحدة()
    {
        var customer = NewCustomer();

        var token = customer.GenerateResetToken(Now);

        token.Should().NotBeNullOrWhiteSpace();
        // القاعدة لا تحمل الرمز نفسه أبداً — من يقرؤها لا يملك رابطاً صالحاً (Phase 0 B6).
        customer.PasswordResetTokenHash.Should().Be(Customer.HashResetToken(token));
        customer.PasswordResetTokenHash.Should().NotBe(token);
        customer.PasswordResetTokenExpiry.Should().Be(Now.AddHours(Customer.ResetTokenLifetimeHours));
    }

    [Fact]
    public void GenerateResetToken_يولّد_رمزاً_عشوائياً_بطول_256_بت()
    {
        var customer = NewCustomer();

        var first = customer.GenerateResetToken(Now);
        var second = customer.GenerateResetToken(Now);

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
    public void ResetPassword_داخل_مدّة_الصلاحية_يُحدّث_التجزئة_ويمسح_حقول_الرمز()
    {
        var customer = NewCustomer();
        customer.GenerateResetToken(Now);

        customer.ResetPassword("new-hashed-value", Now.AddHours(Customer.ResetTokenLifetimeHours).AddSeconds(-1));

        customer.PasswordHash.Should().Be("new-hashed-value");
        customer.PasswordResetTokenHash.Should().BeNull();
        customer.PasswordResetTokenExpiry.Should().BeNull();
    }

    [Fact]
    public void ResetPassword_بعد_انتهاء_الصلاحية_يرمي_ولا_يغيّر_كلمة_المرور()
    {
        var customer = NewCustomer();
        customer.GenerateResetToken(Now);

        var act = () => customer.ResetPassword("new-hashed-value", Now.AddHours(Customer.ResetTokenLifetimeHours).AddSeconds(1));

        act.Should().Throw<InvalidPasswordResetException>();
        customer.PasswordHash.Should().Be("hashed"); // لم يتغيّر
    }

    [Fact]
    public void ResetPassword_بلا_طلب_إعادة_تعيين_قائم_يرمي()
    {
        var customer = NewCustomer(); // لم يُستدعَ GenerateResetToken إطلاقاً

        var act = () => customer.ResetPassword("new-hashed-value", Now);

        act.Should().Throw<InvalidPasswordResetException>();
    }
}
