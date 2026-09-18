using AwesomeAssertions;
using NSubstitute;
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Security;
using Souq.Application.Features.Auth;
using Souq.Application.Tests.TestDoubles;
using Souq.Domain.Common;
using Souq.Domain.Enums;
using Souq.Domain.Identity;
using Souq.Domain.Interfaces;

namespace Souq.Application.Tests.Auth;

// ============================================================================
// `AccountLifecycle` — تنفيذ العقد الذي أعادت M9 به كتابةَ الحساب إلى وحدته (TD-03/R-15).
//
// هذه الاختبارات ليست جديدة في جوهرها: كانت تعيش في `CustomerAccountHandlersTests` وتفحص
// `_user.Status` و`_user.PasswordHash` و`Forget(7)` **من خلال** محو العميل — أي أنّ وحدةً كانت
// تُثبِّت سلوك حسابٍ لا تملكه، وهو الوجه الآخر للعبورات التي كُسرت. انتقلت هنا بلا أن ينقص ما تحرسه.
// ============================================================================
public class AccountLifecycleTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IRefreshTokenRepository _tokens = Substitute.For<IRefreshTokenRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly ISessionValidator _sessions = Substitute.For<ISessionValidator>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly FixedClock _clock = new();
    private readonly User _user = TestCatalog.WithId(
        new User("سارة", "sara@souq.test", "$2a$hash", Roles.Customer), 7);

    private AccountLifecycle Lifecycle() => new(_users, _tokens, _hasher, _sessions, _uow, _clock);

    public AccountLifecycleTests()
    {
        _users.GetByIdAsync(7, Arg.Any<CancellationToken>()).Returns(_user);
        _tokens.ListActiveForUserAsync(7, Arg.Any<CancellationToken>()).Returns(Array.Empty<RefreshToken>());
    }

    [Fact]
    public async Task التحقّق_من_كلمة_المرور_يمرّ_بدالّة_التجزئة_ولا_تخرج_التجزئة_من_الوحدة()
    {
        _hasher.Verify("right", _user.PasswordHash).Returns(true);
        _hasher.Verify("wrong", _user.PasswordHash).Returns(false);

        (await Lifecycle().VerifyPasswordAsync(7, "right", CancellationToken.None)).Should().BeTrue();
        (await Lifecycle().VerifyPasswordAsync(7, "wrong", CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task حساب_غير_موجود_يُعيد_false_لا_استثناء()
    {
        // المتصل يرفض بالرسالة نفسها في الحالتين، فلا يُميّز "لا حساب" من "كلمة خاطئة".
        (await Lifecycle().VerifyPasswordAsync(99, "anything", CancellationToken.None)).Should().BeFalse();
        _hasher.DidNotReceive().Verify(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task إعادة_التسمية_تُعدّل_الحساب_بلا_حفظ()
    {
        await Lifecycle().RenameAsync(7, "سارة محمد", CancellationToken.None);

        _user.FullName.Should().Be("سارة محمد");
        // الحفظ لوحدة عمل المستدعي: تحديث الملفّ واسم الحساب معاً أو لا شيء.
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task إعادة_تسمية_حساب_غير_موجود_لا_تفعل_شيئاً()
    {
        var act = () => Lifecycle().RenameAsync(99, "أيّ اسم", CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task المحو_يجرّد_الحساب_ويوقفه_ويُلغي_رموزه_ثم_ينسى_ختمه_بعد_الحفظ()
    {
        var (token, _) = RefreshToken.Issue(_user, Guid.NewGuid(), _clock.UtcNow, TimeSpan.FromDays(30));
        _tokens.ListActiveForUserAsync(7, Arg.Any<CancellationToken>()).Returns(new[] { token });
        var stamp = _user.SecurityStamp;

        await Lifecycle().EraseAsync(7, CancellationToken.None);

        (_user.Status, _user.PasswordHash).Should().Be((UserStatus.Disabled, ""));
        _user.FullName.Should().Be("حساب محذوف");
        _user.SecurityStamp.Should().NotBe(stamp, "الختم يدوّر فتسقط توكنات الوصول القائمة");
        (token.RevokedAt, token.RevokedReason).Should().Be((_clock.UtcNow, "Erased"));
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        _sessions.Received(1).Forget(7);
    }

    [Fact]
    public async Task المحو_يحفظ_قبل_أن_ينسى_لا_بعده()
    {
        // ترتيبٌ مقصود لا تفصيل: لو سبق النسيانُ الحفظَ، أمكن لطلبٍ متزامن أن يعيد تخزين الختم القديم
        // (قرأه قبل الإيداع) فيبقى توكن وصولٍ مُبطَل مقبولاً حتى ثلاثين ثانية (عمر الذاكرة).
        var order = new List<string>();
        _uow.When(u => u.SaveChangesAsync(Arg.Any<CancellationToken>())).Do(_ => order.Add("save"));
        _sessions.When(s => s.Forget(7)).Do(_ => order.Add("forget"));

        await Lifecycle().EraseAsync(7, CancellationToken.None);

        order.Should().Equal(["save", "forget"]);
    }

    [Fact]
    public async Task محو_حساب_غير_موجود_يحفظ_ما_جهّزه_المستدعي_ولا_ينسى_ختماً()
    {
        // الملفّ قد يكون بلا حساب (بيانات قديمة): تجريد الملفّ ما زال يجب أن يُودَع.
        await Lifecycle().EraseAsync(99, CancellationToken.None);

        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        _sessions.DidNotReceive().Forget(Arg.Any<int>());
    }
}
