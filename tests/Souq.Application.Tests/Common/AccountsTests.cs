using AwesomeAssertions;
using NSubstitute;
using Souq.Application.Common.Accounts;
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Notifications;
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
    private readonly INotificationOutbox _outbox = Substitute.For<INotificationOutbox>();
    private readonly IStorefrontLinks _links = Substitute.For<IStorefrontLinks>();
    private readonly ISessionValidator _sessions = Substitute.For<ISessionValidator>();

    public AccountsTests()
    {
        _links.Origin(Arg.Any<string?>()).Returns(call => $"https://{call.Arg<string?>() ?? "request-host"}");
        _uow.InTransactionAsync(Arg.Any<Func<Task>>(), Arg.Any<CancellationToken>()).Returns(call => call.Arg<Func<Task>>()());
        // والصيغة ذات مستوى العزل (F-29): بلا ضبطها يُعيد الضعف مهمّةً فارغة، فلا يُنفَّذ جسم
        // المعاملة أصلاً — والاختبار يفشل على شيء لم يجرِ بدل أن يقيس ما يدّعي قياسه.
        _uow.InTransactionAsync(Arg.Any<Func<Task>>(), Arg.Any<TransactionIsolation>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<Task>>()());
    }

    private AccountInvitations Invitations() => new(_users, _uow, _outbox, _links, TimeProvider.System);

    private static bool IsAcmeInvitation(object message) =>
        message is AccountInvited { InviterName: "Acme", Origin: "https://acme.test" };

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
        // المرحلة 14: الحساب يُحفظ أولاً (معرّفه في الرسالة) ثم رسالة الدعوة في صندوق الصادر على مضيف المتجر المطلوب — الرمز
        // يُولَّد عند إرسالها.
        Received.InOrder(() =>
        {
            _uow.SaveChangesAsync(Arg.Any<CancellationToken>());
            _outbox.Enqueue(Arg.Is<object>(m => IsAcmeInvitation(m)));
            _uow.SaveChangesAsync(Arg.Any<CancellationToken>());
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
        _outbox.DidNotReceiveWithAnyArgs().Enqueue(default!);
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

    // ========================================================================
    // F-29 — الحالة المحروسة وحدها تطلب SERIALIZABLE.
    //
    // ما يثبته هذا الاختبار هو **القرار**، لا أثره: أنّ العزل الأعلى يُطلَب حين يكون ثابت "آخر
    // مدير" في خطر، ولا يُطلَب لإيقافٍ عاديّ لا ثابت له. أمّا أنّ العزل يحمي فعلاً تحت
    // READ_COMMITTED_SNAPSHOT فلا تقوله إلّا قاعدة حقيقية — وذاك اختبار التكامل
    // `LastAdministratorRcsiTests`، لأنّ ضعفاً في الذاكرة لا يعرف عن الأقفال شيئاً.
    //
    // ولماذا يستحقّ التمييز اختباراً: أقفال المدى ثمنٌ حقيقي (بطء وجمود محتمل)، ودفعُها في كل
    // إيقاف حساب بلا سبب انحدارُ أداء لا يلاحظه أحد حتى يصير بطيئاً في الإنتاج.
    // ========================================================================
    [Fact]
    public async Task إيقاف_آخر_مدير_محتمل_يُطلَب_بعزل_تسلسلي_ولا_يُطلَب_لغيره()
    {
        var recording = new RecordingUnitOfWork();
        var changer = new AccountStatusChanger(
            _users, _tokens, recording, _sessions, TestCurrentUser.Admin(99), TimeProvider.System);

        // حاملُ الدور الأعلى، فعّال وغير معلّق ⇒ محروس. والعدّ اثنان فلا يُرفض على المسار السريع.
        var admin = WithId(new User("مدير", "admin@acme.test", "hash", Roles.TenantAdmin), 7);
        _users.GetByIdAsync(7, Arg.Any<CancellationToken>()).Returns(admin);
        _users.CountActiveByRoleAsync(Roles.TenantAdmin, Arg.Any<CancellationToken>()).Returns(2);
        _tokens.ListActiveForUserAsync(7, Arg.Any<CancellationToken>()).Returns([]);

        (await changer.SetActiveAsync(7, false, Roles.IsStoreStaff, Roles.TenantAdmin, CancellationToken.None))
            .IsSuccess.Should().BeTrue();
        recording.LastIsolation.Should().Be(TransactionIsolation.Serializable,
            "الثابت يُقاس عبر عدّة صفوف، ولا يصحّ إلّا بعزل يمنع تغيّر المدى المعدود");

        // وموظّف عاديّ: لا ثابت عبر صفوف، فلا ثمن أقفال مدى.
        var staff = WithId(new User("موظّف", "staff@acme.test", "hash", Roles.TenantStaff), 8);
        _users.GetByIdAsync(8, Arg.Any<CancellationToken>()).Returns(staff);
        _tokens.ListActiveForUserAsync(8, Arg.Any<CancellationToken>()).Returns([]);

        (await changer.SetActiveAsync(8, false, Roles.IsStoreStaff, Roles.TenantAdmin, CancellationToken.None))
            .IsSuccess.Should().BeTrue();
        recording.LastIsolation.Should().Be(TransactionIsolation.Default,
            "إيقاف حساب لا يحرسه ثابت لا يدفع ثمن أقفال المدى");
    }

    private static T WithId<T>(T entity, int id) where T : Souq.Domain.Common.Entity
    {
        typeof(Souq.Domain.Common.Entity).GetProperty(nameof(Souq.Domain.Common.Entity.Id))!
            .GetSetMethod(nonPublic: true)!.Invoke(entity, [id]);
        return entity;
    }
}
