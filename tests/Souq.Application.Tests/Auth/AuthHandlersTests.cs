using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Models;
using Souq.Application.Common.Notifications;
using Souq.Application.Common.Security;
using Souq.Application.Common.Tenancy;
using Souq.Application.Features.Auth;
using Souq.Application.Features.Auth.Commands;
using Souq.Application.Tests.TestDoubles;
using Souq.Domain.Common;
using Souq.Domain.Entities;
using Souq.Domain.Exceptions;
using Souq.Domain.Identity;
using Souq.Domain.Interfaces;
using Souq.Application.Features.Auth.Contracts;

namespace Souq.Application.Tests.Auth;

// عُدّة مشتركة: مستودعات ومنافذ بديلة + مُصدِر جلسات حقيقي فوقها (المنطق المختبَر، لا بديله).
internal sealed class AuthRig
{
    public IUserRepository Users { get; } = Substitute.For<IUserRepository>();
    // عقد Identity مع Customers لا مستودعها (TD-03، M9): الوحدة لم تعد ترى `ICustomerRepository`.
    public IAccountProfiles Profiles { get; } = Substitute.For<IAccountProfiles>();
    public IRefreshTokenRepository Tokens { get; } = Substitute.For<IRefreshTokenRepository>();
    public IPasswordHasher Hasher { get; } = Substitute.For<IPasswordHasher>();
    public IJwtTokenGenerator Jwt { get; } = Substitute.For<IJwtTokenGenerator>();
    public IUnitOfWork Uow { get; } = Substitute.For<IUnitOfWork>();
    public INotificationOutbox Outbox { get; } = Substitute.For<INotificationOutbox>();
    public IStorefrontLinks Links { get; } = Substitute.For<IStorefrontLinks>();
    public ISessionValidator Sessions { get; } = Substitute.For<ISessionValidator>();
    public FixedClock Clock { get; } = new();
    public List<RefreshToken> Issued { get; } = [];

    public AuthRig()
    {
        Jwt.RefreshTokenLifetime.Returns(TimeSpan.FromDays(30));
        Jwt.Generate(Arg.Any<User>(), Arg.Any<int?>()).Returns(_ => ("access-token", Clock.UtcNow.AddMinutes(15)));
        Tokens.When(t => t.AddAsync(Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>()))
              .Do(call => Issued.Add(call.Arg<RefreshToken>()));
        Tokens.ListActiveForUserAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(new List<RefreshToken>());
        Tokens.ListActiveInFamilyAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new List<RefreshToken>());
        Uow.InTransactionAsync(Arg.Any<Func<Task<AuthSession>>>(), Arg.Any<CancellationToken>())
           .Returns(call => call.Arg<Func<Task<AuthSession>>>()());
        Links.Origin(Arg.Any<string?>()).Returns("https://store.test");
    }

    public AuthSessionIssuer Issuer() => new(Tokens, Profiles, Jwt, Uow, Clock);

    public static User SavedUser(int id, string role = Roles.Customer, string hash = "hashed", string? email = null)
    {
        var user = new User("مستخدم", email ?? $"user{id}@souq.com", hash, role);
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(user, id);
        return user;
    }

    public static string TokenIn(string link) =>
        Uri.UnescapeDataString(link[(link.IndexOf("token=", StringComparison.Ordinal) + "token=".Length)..]);
}

public class RegisterHandlerTests
{
    private readonly AuthRig _rig = new();

    private RegisterHandler Handler(ITenantContext? tenant = null) => new(
        _rig.Users, _rig.Profiles, _rig.Hasher, _rig.Issuer(), _rig.Outbox, _rig.Links,
        tenant ?? TestTenant.Context(), _rig.Uow);

    [Fact]
    public async Task لا_تسجيل_ذاتي_في_منطقة_المنصّة()
    {
        var platform = new TenantContext();
        platform.UsePlatform();

        var result = await Handler(platform).Handle(new RegisterCommand("جديد", "new@souq.com", "Passw0rd!"), CancellationToken.None);

        result.ErrorCode.Should().Be("RegistrationNotAllowed");
        result.Error!.Kind.Should().Be(ErrorKind.Forbidden);
        await _rig.Users.DidNotReceive().AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task بريد_مستخدم_في_المتجر_يُرفض_ولا_يُنشئ_شيئاً()
    {
        _rig.Users.GetByEmailAsync("taken@souq.com", Arg.Any<CancellationToken>()).Returns(AuthRig.SavedUser(1));

        var result = await Handler().Handle(new RegisterCommand("جديد", "taken@souq.com", "Passw0rd!"), CancellationToken.None);

        result.ErrorCode.Should().Be("EmailTaken");
        await _rig.Users.DidNotReceive().AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task تسجيل_صالح_ينشئ_حساب_عميل_وملف_شرائه_وجلسة_ويضع_رسالة_التأكيد_في_الصادر()
    {
        User? added = null;
        (int UserId, string Name, string Email)? profile = null;
        object? queued = null;
        _rig.Hasher.Hash("Passw0rd!").Returns("hashed-value");
        _rig.Users.When(u => u.AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>())).Do(call => added = call.Arg<User>());
        _rig.Uow.When(u => u.SaveChangesAsync(Arg.Any<CancellationToken>())).Do(_ =>
        {
            if (added is { Id: 0 }) typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(added, 42);
        });
        // يُطلب ملفّ العميل بمعرّف الحساب بعد حفظه — وبناءُ التجمّع نفسه صار داخل Customers
        // (CustomerAccountProfilesTests يثبته)، فما يُثبَّت هنا هو العقد: بأيّ معرّفٍ وبأيّ بيانات يُنادى.
        _rig.Profiles.When(p => p.CreateForAccountAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()))
                     .Do(call => profile = (call.ArgAt<int>(0), call.ArgAt<string>(1), call.ArgAt<string>(2)));
        _rig.Profiles.FindIdForAccountAsync(42, Arg.Any<CancellationToken>()).Returns(7);
        _rig.Outbox.When(o => o.Enqueue(Arg.Any<object>())).Do(call => queued = call.Arg<object>());

        var result = await Handler().Handle(new RegisterCommand("مستخدم جديد", "New@Souq.com", "Passw0rd!"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        added!.Role.Should().Be(Roles.Customer);
        added.Email.Should().Be("new@souq.com");
        added.PasswordHash.Should().Be("hashed-value");
        profile.Should().Be((42, "مستخدم جديد", "new@souq.com"));
        result.Value!.Response.User.CustomerId.Should().Be(7);
        result.Value.Response.AccessToken.Should().Be("access-token");
        result.Value.RefreshToken.Should().NotBeNullOrWhiteSpace();
        _rig.Issued.Should().ContainSingle().Which.TokenHash.Should().Be(User.HashToken(result.Value.RefreshToken));
        // المرحلة 14: رسالة التأكيد مرجع (الحساب المحفوظ + أصل الواجهة) في صندوق الصادر؛ الرمز يُولَّد عند إرسالها.
        queued.Should().Be(new EmailVerificationRequested(42, "https://store.test"));
        added.EmailVerificationTokenHash.Should().BeNull();
    }
}

public class LoginHandlerTests
{
    private readonly AuthRig _rig = new();

    private LoginHandler Handler() => new(_rig.Users, _rig.Hasher, _rig.Issuer(), _rig.Uow, _rig.Clock,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<LoginHandler>.Instance);

    [Fact]
    public async Task بريد_غير_مسجّل_رسالة_موحّدة_بزمن_التحقّق_نفسه()
    {
        var result = await Handler().Handle(new LoginCommand("nobody@souq.com", "whatever"), CancellationToken.None);

        result.ErrorCode.Should().Be("InvalidCredentials");
        _rig.Hasher.Received(1).Verify("whatever", "");
        _rig.Issued.Should().BeEmpty();
    }

    [Fact]
    public async Task كلمة_خاطئة_تُحسب_محاولة_فاشلة_وتُحفظ_بالرسالة_الموحّدة()
    {
        var user = AuthRig.SavedUser(5);
        _rig.Users.GetByEmailAsync("user5@souq.com", Arg.Any<CancellationToken>()).Returns(user);

        var result = await Handler().Handle(new LoginCommand("user5@souq.com", "wrong"), CancellationToken.None);

        result.ErrorCode.Should().Be("InvalidCredentials");
        user.FailedLoginCount.Should().Be(1);
        await _rig.Uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task الحساب_المقفل_يُرفض_حتى_بالكلمة_الصحيحة()
    {
        var user = AuthRig.SavedUser(5);
        for (var i = 0; i < User.MaxFailedLogins; i++) user.RecordFailedLogin(_rig.Clock.UtcNow);
        _rig.Users.GetByEmailAsync("user5@souq.com", Arg.Any<CancellationToken>()).Returns(user);
        _rig.Hasher.Verify("correct", "hashed").Returns(true);

        var result = await Handler().Handle(new LoginCommand("user5@souq.com", "correct"), CancellationToken.None);

        result.ErrorCode.Should().Be("AccountLocked");
        _rig.Issued.Should().BeEmpty();
    }

    [Fact]
    public async Task الحساب_المعطّل_يُكشف_بعد_الكلمة_الصحيحة_فقط()
    {
        var user = AuthRig.SavedUser(5);
        user.Disable();
        _rig.Users.GetByEmailAsync("user5@souq.com", Arg.Any<CancellationToken>()).Returns(user);
        _rig.Hasher.Verify("correct", "hashed").Returns(true);

        (await Handler().Handle(new LoginCommand("user5@souq.com", "wrong"), CancellationToken.None))
            .ErrorCode.Should().Be("InvalidCredentials");
        (await Handler().Handle(new LoginCommand("user5@souq.com", "correct"), CancellationToken.None))
            .ErrorCode.Should().Be("AccountDisabled");
    }

    [Fact]
    public async Task دخول_صالح_يصدر_جلسة_بصلاحيات_الدور_ويصفّر_العدّاد()
    {
        var user = AuthRig.SavedUser(5, Roles.TenantStaff);
        user.RecordFailedLogin(_rig.Clock.UtcNow);
        _rig.Users.GetByEmailAsync("user5@souq.com", Arg.Any<CancellationToken>()).Returns(user);
        _rig.Hasher.Verify("correct", "hashed").Returns(true);

        var result = await Handler().Handle(new LoginCommand("user5@souq.com", "correct"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Response.User.Permissions.Should().Contain(Permissions.Orders.Manage)
            .And.NotContain(Permissions.Store.Settings);
        result.Value.Response.User.Area.Should().Be(UserInfo.StoreArea);
        user.FailedLoginCount.Should().Be(0);
        user.LastLoginAt.Should().Be(_rig.Clock.UtcNow);
        _rig.Issued.Should().ContainSingle();
    }
}

public class RefreshSessionHandlerTests
{
    private readonly AuthRig _rig = new();

    private RefreshSessionHandler Handler() => new(
        _rig.Tokens, _rig.Users, _rig.Issuer(), _rig.Sessions, _rig.Uow, _rig.Clock, NullLogger<RefreshSessionHandler>.Instance);

    private (User User, RefreshToken Token, string Raw) Arrange()
    {
        var user = AuthRig.SavedUser(9);
        var (token, raw) = RefreshToken.Issue(user, Guid.NewGuid(), _rig.Clock.UtcNow, TimeSpan.FromDays(30));
        _rig.Tokens.GetByTokenAsync(raw, Arg.Any<CancellationToken>()).Returns(token);
        _rig.Users.GetByIdAsync(9, Arg.Any<CancellationToken>()).Returns(user);
        return (user, token, raw);
    }

    [Fact]
    public async Task بلا_رمز_أو_برمز_مجهول_انتهت_الجلسة()
    {
        (await Handler().Handle(new RefreshSessionCommand(null), CancellationToken.None)).ErrorCode.Should().Be("InvalidRefreshToken");
        (await Handler().Handle(new RefreshSessionCommand("unknown"), CancellationToken.None)).ErrorCode.Should().Be("InvalidRefreshToken");
    }

    [Fact]
    public async Task الرمز_الفعّال_يُستهلك_ويُصدر_تاليه_في_العائلة_نفسها()
    {
        var (_, token, raw) = Arrange();

        var result = await Handler().Handle(new RefreshSessionCommand(raw), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        token.UsedAt.Should().Be(_rig.Clock.UtcNow);
        _rig.Issued.Should().ContainSingle().Which.FamilyId.Should().Be(token.FamilyId);
        result.Value!.RefreshToken.Should().NotBe(raw);
    }

    [Fact]
    public async Task الرمز_نفسه_خلال_ثوانٍ_سباق_تبويبات_لا_سرقة()
    {
        var (user, token, raw) = Arrange();
        token.MarkUsed(_rig.Clock.UtcNow);
        // سباقٌ بريء يعني أنّ تدوير التبويب الأول **نجح**، فالعائلة تحمل خليفةً فعّالاً. كان هذا الشرط
        // مضمَراً في التجهيز (قائمة فارغة) وصار صريحاً في M9: هو بعينه ما يفرّق السباق عن جلسةٍ أُبطلت.
        var successor = RefreshToken.Issue(user, token.FamilyId, _rig.Clock.UtcNow, TimeSpan.FromDays(30)).Token;
        _rig.Tokens.ListActiveInFamilyAsync(token.FamilyId, Arg.Any<CancellationToken>())
            .Returns(new List<RefreshToken> { successor });
        _rig.Clock.Advance(TimeSpan.FromSeconds(5));

        var result = await Handler().Handle(new RefreshSessionCommand(raw), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        token.RevokedAt.Should().BeNull();
    }

    [Fact]
    public async Task رمز_مستهلَك_داخل_المهلة_وعائلته_أُبطلت_لا_يُحيي_الجلسة_ولا_يُعَدّ_سرقة()
    {
        // الثقب الذي أُغلق في M9: تغيير كلمة المرور يُبطل الرموز **غير المستهلكة** وحدها، فرمزٌ دوّره
        // جهازٌ آخر قبل ثوانٍ كان يمرّ من هذا الفرع ويصنع جلسة صالحة تماماً. الشرط الآن أن تكون العائلة
        // حيّة — ولا هي حيّة بعد إبطالٍ صريح.
        var (user, token, raw) = Arrange();
        token.MarkUsed(_rig.Clock.UtcNow);
        _rig.Clock.Advance(TimeSpan.FromSeconds(5));
        // لا خليفة فعّال: هكذا تبدو العائلة بعد RevokeAllAsync (التجهيز يعيد قائمة فارغة أصلاً).
        var stamp = user.SecurityStamp;

        var result = await Handler().Handle(new RefreshSessionCommand(raw), CancellationToken.None);

        result.ErrorCode.Should().Be("InvalidRefreshToken", "الجلسة أُنهيت — لا إحياء");
        _rig.Issued.Should().BeEmpty("ولا جلسة جديدة تُصدر");
        // وليست سرقةً: لو دُوِّر الختم لطُرد صاحبُ تغيير كلمة المرور من جلسته الجديدة بتبويبٍ على جهاز أخرجه.
        user.SecurityStamp.Should().Be(stamp);
        token.RevokedAt.Should().BeNull();
    }

    [Fact]
    public async Task رمز_مستهلك_قديماً_يعود_فتسقط_العائلة_وتوكنات_الوصول()
    {
        var (user, token, raw) = Arrange();
        token.MarkUsed(_rig.Clock.UtcNow);
        var (sibling, _) = RefreshToken.Issue(user, token.FamilyId, _rig.Clock.UtcNow, TimeSpan.FromDays(30));
        _rig.Tokens.ListActiveInFamilyAsync(token.FamilyId, Arg.Any<CancellationToken>()).Returns(new List<RefreshToken> { sibling });
        var stamp = user.SecurityStamp;
        _rig.Clock.Advance(TimeSpan.FromMinutes(1));

        var result = await Handler().Handle(new RefreshSessionCommand(raw), CancellationToken.None);

        result.ErrorCode.Should().Be("RefreshTokenReused");
        token.RevokedAt.Should().NotBeNull();
        sibling.RevokedAt.Should().NotBeNull();
        user.SecurityStamp.Should().NotBe(stamp);
        _rig.Sessions.Received(1).Forget(9);
        _rig.Issued.Should().BeEmpty();
    }

    [Fact]
    public async Task الحساب_المعطّل_لا_يجدّد()
    {
        var (user, _, raw) = Arrange();
        user.Disable();

        (await Handler().Handle(new RefreshSessionCommand(raw), CancellationToken.None)).ErrorCode.Should().Be("InvalidRefreshToken");
    }
}

public class LogoutHandlerTests
{
    private readonly AuthRig _rig = new();

    [Fact]
    public async Task الخروج_يُبطل_العائلة_كلها_وبلا_رمز_نجاح_بلا_حفظ()
    {
        var user = AuthRig.SavedUser(3);
        var family = Guid.NewGuid();
        var (token, raw) = RefreshToken.Issue(user, family, _rig.Clock.UtcNow, TimeSpan.FromDays(30));
        var (sibling, _) = RefreshToken.Issue(user, family, _rig.Clock.UtcNow, TimeSpan.FromDays(30));
        _rig.Tokens.GetByTokenAsync(raw, Arg.Any<CancellationToken>()).Returns(token);
        _rig.Tokens.ListActiveInFamilyAsync(family, Arg.Any<CancellationToken>()).Returns(new List<RefreshToken> { token, sibling });
        var handler = new LogoutHandler(_rig.Tokens, _rig.Uow, _rig.Clock);

        (await handler.Handle(new LogoutCommand(null), CancellationToken.None)).IsSuccess.Should().BeTrue();
        await _rig.Uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());

        (await handler.Handle(new LogoutCommand(raw), CancellationToken.None)).IsSuccess.Should().BeTrue();
        token.RevokedReason.Should().Be("Logout");
        sibling.RevokedReason.Should().Be("Logout");
        await _rig.Uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}

public class ChangePasswordHandlerTests
{
    private readonly AuthRig _rig = new();

    private ChangePasswordHandler Handler(ICurrentUser user) =>
        new(_rig.Users, _rig.Hasher, _rig.Issuer(), _rig.Sessions, user, _rig.Outbox, _rig.Links);

    [Fact]
    public async Task الكلمة_الحالية_الخاطئة_خطأ_إدخال_لا_انتهاء_جلسة()
    {
        var user = AuthRig.SavedUser(11, Roles.TenantAdmin);
        var stamp = user.SecurityStamp;
        _rig.Users.GetByIdAsync(11, Arg.Any<CancellationToken>()).Returns(user);

        var result = await Handler(TestCurrentUser.Admin(11))
            .Handle(new ChangePasswordCommand("wrong", "NewPassw0rd!"), CancellationToken.None);

        result.ErrorCode.Should().Be("CurrentPasswordIncorrect");
        result.Error!.Kind.Should().Be(ErrorKind.Validation);
        user.SecurityStamp.Should().Be(stamp);
    }

    [Fact]
    public async Task التغيير_يُسقط_كل_الجلسات_الأخرى_ويصدر_جلسة_جديدة_لهذا_الجهاز()
    {
        var user = AuthRig.SavedUser(11, Roles.TenantAdmin);
        var stamp = user.SecurityStamp;
        var (otherSession, _) = RefreshToken.Issue(user, Guid.NewGuid(), _rig.Clock.UtcNow, TimeSpan.FromDays(30));
        _rig.Users.GetByIdAsync(11, Arg.Any<CancellationToken>()).Returns(user);
        _rig.Tokens.ListActiveForUserAsync(11, Arg.Any<CancellationToken>()).Returns(new List<RefreshToken> { otherSession });
        _rig.Hasher.Verify("old-pass-1", "hashed").Returns(true);
        _rig.Hasher.Hash("NewPassw0rd!").Returns("new-hash");

        var result = await Handler(TestCurrentUser.Admin(11))
            .Handle(new ChangePasswordCommand("old-pass-1", "NewPassw0rd!"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        user.PasswordHash.Should().Be("new-hash");
        user.SecurityStamp.Should().NotBe(stamp);
        otherSession.RevokedReason.Should().Be("PasswordChanged");
        _rig.Issued.Should().ContainSingle().Which.FamilyId.Should().NotBe(otherSession.FamilyId);
        _rig.Sessions.Received(1).Forget(11);
    }
    // ========================================================================
    // صاحب الحساب يُخطَر بتغيّر كلمة مروره (M15، ASVS 2.2.3).
    //
    // الرسالة قيمتها كلّها في الحالة التي لا يكون فيها المستلِم هو الفاعل — ولذلك تُودَع في الحفظ نفسه
    // الذي يُغيّر الكلمة: تغييرٌ بلا إشعار يعني استيلاءً صامتاً، وإشعارٌ بلا تغيير يعني إنذاراً كاذباً.
    // ========================================================================
    [Fact]
    public async Task تغيير_كلمة_المرور_يُخطِر_صاحب_الحساب()
    {
        var user = AuthRig.SavedUser(11, email: "owner@example.net");
        _rig.Users.GetByIdAsync(11, Arg.Any<CancellationToken>()).Returns(user);
        _rig.Hasher.Verify("Current-Pass-1", Arg.Any<string>()).Returns(true);
        _rig.Hasher.Hash(Arg.Any<string>()).Returns("hashed-new-password");

        var result = await Handler(TestCurrentUser.Customer(11)).Handle(
            new ChangePasswordCommand("Current-Pass-1", "Brand-New-Pass-2"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _rig.Outbox.Received(1).Enqueue(Arg.Is<PasswordChanged>(m => m.UserId == 11));
    }

}

public class ForgotPasswordHandlerTests
{
    private readonly AuthRig _rig = new();

    private ForgotPasswordHandler Handler() => new(_rig.Users, _rig.Outbox, _rig.Links, _rig.Uow, _rig.Clock);

    [Fact]
    public async Task حساب_فعّال_تُوضع_رسالته_في_الصادر_على_مضيف_المتجر_بلا_رمز_وقت_الطلب()
    {
        // المرحلة 14: الطلب لا ينتظر مزوّد البريد، ولا يولّد رمزاً يُخزَّن خاماً في الصندوق — الرمز وتجزئته عند الإرسال
        // (PasswordResetEmailHandler، واختبار التكامل يثبت أن التجزئة وحدها في القاعدة، Phase 0 B6).
        var user = AuthRig.SavedUser(4, email: "distinct.buyer@example.net");
        _rig.Users.GetByEmailAsync("distinct.buyer@example.net", Arg.Any<CancellationToken>()).Returns(user);

        var result = await Handler().Handle(new ForgotPasswordCommand("distinct.buyer@example.net"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _rig.Outbox.Received(1).Enqueue(new PasswordResetRequested(4, "https://store.test"));
        user.PasswordResetTokenHash.Should().BeNull();
        await _rig.Uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task بريد_غير_مسجّل_أو_حساب_معطّل_نجاح_بلا_بريد()
    {
        var disabled = AuthRig.SavedUser(4, email: "disabled@souq.com");
        disabled.Disable();
        _rig.Users.GetByEmailAsync("disabled@souq.com", Arg.Any<CancellationToken>()).Returns(disabled);

        (await Handler().Handle(new ForgotPasswordCommand("nobody@souq.com"), CancellationToken.None)).IsSuccess.Should().BeTrue();
        (await Handler().Handle(new ForgotPasswordCommand("disabled@souq.com"), CancellationToken.None)).IsSuccess.Should().BeTrue();

        _rig.Outbox.DidNotReceiveWithAnyArgs().Enqueue(default!);
        await _rig.Uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
    // ========================================================================
    // مهلة إعادة الإرسال تحرس **صاحب البريد** لا الخادم (M15).
    //
    // حدّ المعدّل في الـ API مقسوم على مضيف المهاجم وعنوانه — يحرس الخادم من مهاجمٍ واحد، ولا يمنعه
    // من إغراق صندوق ضحيّةٍ بعينها برسائل صحيحة تماماً، يدفن بينها ما تحتاجه فعلاً، ويحرق حصّة المتجر
    // عند مزوّد البريد وسمعة مُرسِله — فتسوء وصوليّة كل رسائل ذلك المتجر لكل زبائنه.
    //
    // والردّ يبقى نجاحاً في الحالتين: التفريق بين "أُرسلت" و"لم تُرسل" يكشف وجود الحساب، وهو ما يمنعه
    // هذا المعالج أصلاً.
    // ========================================================================
    [Fact]
    public async Task طلبٌ_ثانٍ_خلال_المهلة_لا_يُودع_رسالةً_ثانية_ويبقى_ردّه_نجاحاً()
    {
        var user = AuthRig.SavedUser(7, email: "flooded@example.net");
        // رمزٌ أُصدر للتوّ (كأنّ رسالةً سابقة أُرسلت قبل لحظات).
        user.GenerateResetToken(_rig.Clock.UtcNow);
        _rig.Users.GetByEmailAsync("flooded@example.net", Arg.Any<CancellationToken>()).Returns(user);

        var result = await Handler().Handle(new ForgotPasswordCommand("flooded@example.net"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue("الردّ لا يتغيّر أبداً — وإلا كُشف وجود الحساب");
        _rig.Outbox.DidNotReceive().Enqueue(Arg.Any<object>());
    }

    [Fact]
    public async Task بعد_انقضاء_المهلة_يُودع_طلبٌ_جديد()
    {
        var user = AuthRig.SavedUser(8, email: "patient@example.net");
        user.GenerateResetToken(_rig.Clock.UtcNow.AddMinutes(-(User.ResetResendCooldownMinutes + 1)));
        _rig.Users.GetByEmailAsync("patient@example.net", Arg.Any<CancellationToken>()).Returns(user);

        (await Handler().Handle(new ForgotPasswordCommand("patient@example.net"), CancellationToken.None))
            .IsSuccess.Should().BeTrue();

        _rig.Outbox.Received(1).Enqueue(Arg.Any<PasswordResetRequested>());
    }

}

public class ResetPasswordHandlerTests
{
    private readonly AuthRig _rig = new();

    private ResetPasswordHandler Handler() =>
        new(_rig.Users, _rig.Hasher, _rig.Issuer(), _rig.Sessions, _rig.Uow, _rig.Clock, _rig.Outbox, _rig.Links);

    [Fact]
    public async Task رمز_مجهول_InvalidResetToken()
    {
        var result = await Handler().Handle(new ResetPasswordCommand("bad-token", "NewPassw0rd!"), CancellationToken.None);

        result.ErrorCode.Should().Be("InvalidResetToken");
        result.Error!.Kind.Should().Be(ErrorKind.BusinessRule);
    }

    [Fact]
    public async Task رمز_منتهٍ_يرفضه_الكيان_ولا_يُحفظ_شيء()
    {
        var user = AuthRig.SavedUser(6, hash: "old-hash");
        user.GenerateResetToken(_rig.Clock.UtcNow);
        _rig.Clock.Advance(TimeSpan.FromHours(User.ResetTokenLifetimeHours).Add(TimeSpan.FromMinutes(1)));
        _rig.Users.GetByResetTokenAsync("expired-token", Arg.Any<CancellationToken>()).Returns(user);

        var act = () => Handler().Handle(new ResetPasswordCommand("expired-token", "NewPassw0rd!"), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidPasswordResetException>()).Which.Code.Should().Be("ResetTokenExpired");
        user.PasswordHash.Should().Be("old-hash");
        await _rig.Uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task رمز_صالح_يغيّر_الكلمة_ويُسقط_كل_الجلسات()
    {
        var user = AuthRig.SavedUser(6, hash: "old-hash");
        var token = user.GenerateResetToken(_rig.Clock.UtcNow);
        var (session, _) = RefreshToken.Issue(user, Guid.NewGuid(), _rig.Clock.UtcNow, TimeSpan.FromDays(30));
        _rig.Users.GetByResetTokenAsync(token, Arg.Any<CancellationToken>()).Returns(user);
        _rig.Tokens.ListActiveForUserAsync(6, Arg.Any<CancellationToken>()).Returns(new List<RefreshToken> { session });
        _rig.Hasher.Hash("NewPassw0rd!").Returns("new-hash");

        var result = await Handler().Handle(new ResetPasswordCommand(token, "NewPassw0rd!"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        user.PasswordHash.Should().Be("new-hash");
        user.PasswordResetTokenHash.Should().BeNull();
        session.RevokedReason.Should().Be("PasswordReset");
        _rig.Sessions.Received(1).Forget(6);
        await _rig.Uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
    // إعادة التعيين تُخطِر أيضاً — وهي الأهمّ: طريقُ من استولى على صندوق بريد، وتُنهي كل الجلسات،
    // فيجد صاحب الحساب نفسه خارجه بلا سببٍ ظاهر ما لم تصله هذه الرسالة.
    [Fact]
    public async Task إعادة_تعيين_كلمة_المرور_تُخطِر_صاحب_الحساب()
    {
        var user = AuthRig.SavedUser(12, email: "reset.owner@example.net");
        var token = user.GenerateResetToken(_rig.Clock.UtcNow);
        _rig.Hasher.Hash(Arg.Any<string>()).Returns("hashed-new-password");
        _rig.Users.GetByResetTokenAsync(token, Arg.Any<CancellationToken>()).Returns(user);

        (await Handler().Handle(new ResetPasswordCommand(token, "Brand-New-Pass-3"), CancellationToken.None))
            .IsSuccess.Should().BeTrue();

        _rig.Outbox.Received(1).Enqueue(Arg.Is<PasswordChanged>(m => m.UserId == 12));
    }

}

public class VerifyEmailHandlerTests
{
    private readonly AuthRig _rig = new();

    [Fact]
    public async Task رمز_مجهول_يُرفض_ورمز_صالح_يؤكّد_البريد()
    {
        var user = AuthRig.SavedUser(8);
        var token = user.GenerateEmailVerificationToken(_rig.Clock.UtcNow);
        _rig.Users.GetByVerificationTokenAsync(token, Arg.Any<CancellationToken>()).Returns(user);
        var handler = new VerifyEmailHandler(_rig.Users, _rig.Uow, _rig.Clock);

        (await handler.Handle(new VerifyEmailCommand("unknown"), CancellationToken.None))
            .ErrorCode.Should().Be("InvalidVerificationToken");
        (await handler.Handle(new VerifyEmailCommand(token), CancellationToken.None)).IsSuccess.Should().BeTrue();

        user.EmailConfirmedAt.Should().Be(_rig.Clock.UtcNow);
    }
}
