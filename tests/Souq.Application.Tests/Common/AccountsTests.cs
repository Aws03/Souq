using AwesomeAssertions;
using NSubstitute;
using Souq.Application.Common.Accounts;
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Security;
using Souq.Application.Tests.TestDoubles;
using Souq.Domain.Common;
using Souq.Domain.Identity;
using Souq.Domain.Interfaces;

namespace Souq.Application.Tests.Common;

// حسابات الإدارة: الدعوة (جديدة، تجديد لدعوة معلّقة، رفض لحساب مفعّل) والتفعيل/الإيقاف (لا إيقاف للنفس ولا لآخر
// مدير، والإيقاف يُبطل رموز التجديد ويُسقط الختم المخزَّن).
public class AccountsTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IRefreshTokenRepository _tokens = Substitute.For<IRefreshTokenRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IEmailService _email = Substitute.For<IEmailService>();
    private readonly IStorefrontLinks _links = Substitute.For<IStorefrontLinks>();
    private readonly ISessionValidator _sessions = Substitute.For<ISessionValidator>();

    public AccountsTests() =>
        _links.Invitation(Arg.Any<string>(), Arg.Any<string?>())
            .Returns(call => $"https://{call.ArgAt<string?>(1) ?? "request-host"}/accept-invitation?token={call.ArgAt<string>(0)}");

    private AccountInvitations Invitations() => new(_users, _uow, _email, _links, TimeProvider.System);

    private AccountStatusChanger Changer(ICurrentUser currentUser) =>
        new(_users, _tokens, _uow, _sessions, currentUser, TimeProvider.System);

    [Fact]
    public async Task دعوة_جديدة_تُحفظ_ثم_تُرسل_على_المضيف_المطلوب()
    {
        _users.GetByEmailAsync("boss@acme.test", Arg.Any<CancellationToken>()).Returns((User?)null);

        var result = await Invitations().InviteAsync("مدير", "boss@acme.test", Roles.TenantAdmin, "Acme", "acme.test", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Renewed.Should().BeFalse();
        await _users.Received(1).AddAsync(Arg.Is<User>(u => u.IsInvitationPending && u.Role == Roles.TenantAdmin), Arg.Any<CancellationToken>());
        Received.InOrder(() =>
        {
            _uow.SaveChangesAsync(Arg.Any<CancellationToken>());
            _email.SendInvitationAsync("boss@acme.test", "Acme",
                Arg.Is<string>(link => link.StartsWith("https://acme.test/accept-invitation?token=")), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task دعوة_معلّقة_تُجدَّد_بدورها_الجديد()
    {
        var (pending, oldToken) = User.Invite("موظّف", "staff@acme.test", Roles.TenantStaff, DateTime.UtcNow);
        _users.GetByEmailAsync("staff@acme.test", Arg.Any<CancellationToken>()).Returns(pending);

        var result = await Invitations().InviteAsync("مدير جديد", "staff@acme.test", Roles.TenantAdmin, "Acme", null, CancellationToken.None);

        result.Value!.Renewed.Should().BeTrue();
        pending.Role.Should().Be(Roles.TenantAdmin);
        pending.PasswordResetTokenHash.Should().NotBe(User.HashToken(oldToken));
        await _users.DidNotReceive().AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task بريد_حساب_مفعّل_يُرفض_بلا_رسالة()
    {
        _users.GetByEmailAsync("active@acme.test", Arg.Any<CancellationToken>())
            .Returns(new User("مفعّل", "active@acme.test", "$2a$11$hash", Roles.TenantStaff));

        var result = await Invitations().InviteAsync("x", "active@acme.test", Roles.TenantStaff, "Acme", null, CancellationToken.None);

        result.ErrorCode.Should().Be("EmailTaken");
        await _email.DidNotReceiveWithAnyArgs().SendInvitationAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task لا_يوقف_أحد_حسابه_بنفسه()
    {
        var self = new User("أنا", "me@acme.test", "$2a$11$hash", Roles.TenantAdmin);   // Id = 0
        _users.GetByIdAsync(0, Arg.Any<CancellationToken>()).Returns(self);

        var result = await Changer(TestCurrentUser.Admin(userId: 0)).SetActiveAsync(0, false, Roles.IsStoreStaff, Roles.TenantAdmin, CancellationToken.None);

        result.ErrorCode.Should().Be("CannotDisableSelf");
        self.Status.Should().Be(UserStatus.Active);
    }

    [Fact]
    public async Task لا_يُوقف_آخر_مدير_فعّال()
    {
        var lastAdmin = new User("المدير", "boss@acme.test", "$2a$11$hash", Roles.TenantAdmin);
        _users.GetByIdAsync(0, Arg.Any<CancellationToken>()).Returns(lastAdmin);
        _users.CountActiveByRoleAsync(Roles.TenantAdmin, Arg.Any<CancellationToken>()).Returns(1);

        var result = await Changer(TestCurrentUser.Admin()).SetActiveAsync(0, false, Roles.IsStoreStaff, Roles.TenantAdmin, CancellationToken.None);

        result.ErrorCode.Should().Be("LastAdministrator");
    }

    [Fact]
    public async Task حساب_خارج_الأدوار_المُدارة_غير_موجود()
    {
        _users.GetByIdAsync(0, Arg.Any<CancellationToken>())
            .Returns(new User("عميل", "c@acme.test", "$2a$11$hash", Roles.Customer));

        var result = await Changer(TestCurrentUser.Admin()).SetActiveAsync(0, false, Roles.IsStoreStaff, Roles.TenantAdmin, CancellationToken.None);

        result.ErrorCode.Should().Be("NotFound");
    }

    [Fact]
    public async Task الإيقاف_يدوّر_الختم_ويُبطل_رموز_التجديد_ويُسقط_الذاكرة()
    {
        var staff = new User("موظّف", "s@acme.test", "$2a$11$hash", Roles.TenantStaff);
        var stamp = staff.SecurityStamp;
        var (token, _) = RefreshToken.Issue(staff, Guid.NewGuid(), DateTime.UtcNow, TimeSpan.FromDays(30));
        _users.GetByIdAsync(0, Arg.Any<CancellationToken>()).Returns(staff);
        _tokens.ListActiveForUserAsync(0, Arg.Any<CancellationToken>()).Returns([token]);

        var result = await Changer(TestCurrentUser.Admin()).SetActiveAsync(0, false, Roles.IsStoreStaff, Roles.TenantAdmin, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        staff.Status.Should().Be(UserStatus.Disabled);
        staff.SecurityStamp.Should().NotBe(stamp);
        token.RevokedAt.Should().NotBeNull();
        _sessions.Received(1).Forget(0);
    }
}
