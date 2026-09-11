using AwesomeAssertions;
using Souq.Domain.Common;
using Souq.Domain.Identity;

namespace Souq.Domain.Tests;

// رمز التجديد: تجزئة فقط في القاعدة، استهلاك واحد بالتدوير، ومهلة سباق قصيرة قبل اعتبار الإعادة سرقة.
public class RefreshTokenTests
{
    private static readonly DateTime Now = new(2026, 9, 11, 10, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    private static User NewUser(string role = Roles.Customer) => new("مستخدم", "user@souq.com", "hashed", role);

    [Fact]
    public void الإصدار_يخزّن_التجزئة_ويعيد_القيمة_الخام_مرة_واحدة()
    {
        var family = Guid.NewGuid();

        var (token, raw) = RefreshToken.Issue(NewUser(), family, Now, Lifetime);

        raw.Should().HaveLength(43);
        token.TokenHash.Should().Be(User.HashToken(raw)).And.NotBe(raw);
        token.FamilyId.Should().Be(family);
        token.ExpiresAt.Should().Be(Now.Add(Lifetime));
        token.IsActive(Now).Should().BeTrue();
        token.IsActive(Now.Add(Lifetime)).Should().BeFalse();
        token.BelongsToPlatform.Should().BeFalse();
        RefreshToken.Issue(NewUser(Roles.PlatformOwner), family, Now, Lifetime).Token.BelongsToPlatform.Should().BeTrue();
    }

    [Fact]
    public void المستهلَك_ليس_فعّالاً_ومهلة_السباق_ثوانٍ_فقط()
    {
        var (token, _) = RefreshToken.Issue(NewUser(), Guid.NewGuid(), Now, Lifetime);

        token.MarkUsed(Now);

        token.IsActive(Now).Should().BeFalse();
        token.IsWithinReuseGrace(Now.Add(RefreshToken.ReuseGracePeriod)).Should().BeTrue();
        token.IsWithinReuseGrace(Now.Add(RefreshToken.ReuseGracePeriod).AddSeconds(1)).Should().BeFalse();
    }

    [Fact]
    public void الإبطال_نهائي_ويحفظ_سببه_الأول()
    {
        var (token, _) = RefreshToken.Issue(NewUser(), Guid.NewGuid(), Now, Lifetime);
        token.MarkUsed(Now);

        token.Revoke("ReuseDetected", Now);
        token.Revoke("Logout", Now.AddMinutes(1));

        token.RevokedReason.Should().Be("ReuseDetected");
        token.RevokedAt.Should().Be(Now);
        token.IsWithinReuseGrace(Now).Should().BeFalse();
    }
}
