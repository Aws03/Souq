using AwesomeAssertions;
using Souq.Domain.Auditing;
using Souq.Domain.Common;
using Souq.Domain.Exceptions;
using Souq.Domain.Identity;

namespace Souq.Domain.Tests;

// دعوة حساب إدارة: حساب بلا كلمة مرور ينتظر قبول صاحب البريد برمز مجزَّأ صالح 72 ساعة؛ القبول يمرّ بإعادة
// التعيين فيؤكّد البريد. وسطر التدقيق: فعل بصيغة ثابتة، منطقة معروفة، وقصّ لا رفض.
public class InvitationAndAuditTests
{
    private static readonly DateTime Now = new(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void الدعوة_حساب_بلا_كلمة_مرور_برمز_مجزَّأ_لـ72_ساعة()
    {
        var (user, token) = User.Invite("مدير المتجر", "Boss@Store.Example", Roles.TenantAdmin, Now);

        user.IsInvitationPending.Should().BeTrue();
        user.Email.Should().Be("boss@store.example");
        user.PasswordResetTokenHash.Should().Be(User.HashToken(token)).And.NotBe(token);
        user.PasswordResetTokenExpiry.Should().Be(Now.AddHours(User.InvitationTokenLifetimeHours));
        user.EmailConfirmedAt.Should().BeNull();
    }

    [Fact]
    public void لا_دعوات_لحسابات_العملاء()
    {
        var act = () => User.Invite("عميل", "c@store.example", Roles.Customer, Now);

        act.Should().Throw<InvalidIdentityOperationException>();
    }

    [Fact]
    public void قبول_الدعوة_يضبط_كلمة_المرور_ويؤكّد_البريد_ويُبطل_الرمز()
    {
        var (user, _) = User.Invite("موظّف", "staff@store.example", Roles.TenantStaff, Now);

        user.ResetPassword("$2a$11$hash", Now.AddHours(1));

        user.IsInvitationPending.Should().BeFalse();
        user.EmailConfirmedAt.Should().Be(Now.AddHours(1));
        user.PasswordResetTokenHash.Should().BeNull();
    }

    [Fact]
    public void تجديد_الدعوة_لحساب_ينتظر_فقط()
    {
        var (pending, first) = User.Invite("موظّف", "staff@store.example", Roles.TenantStaff, Now);
        var second = pending.RenewInvitation(Now.AddHours(10));

        second.Should().NotBe(first);
        pending.PasswordResetTokenHash.Should().Be(User.HashToken(second));

        pending.ResetPassword("$2a$11$hash", Now.AddHours(11));
        ((Action)(() => pending.RenewInvitation(Now.AddHours(12)))).Should().Throw<InvalidIdentityOperationException>();
    }

    [Theory]
    [InlineData("Tenant.Created")]
    [InlineData("tenant")]
    [InlineData("tenant created")]
    [InlineData("")]
    public void فعل_تدقيق_بصيغة_غير_ثابتة_يُرفض(string action)
    {
        var act = () => new AuditEntry(Now, AuditAreas.Platform, action, null, 1, "PlatformOwner", null, null, null, null, null);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void منطقة_تدقيق_مجهولة_تُرفض_والقيم_الطويلة_تُقصّ()
    {
        var unknownArea = () => new AuditEntry(Now, "Moon", "tenant.created", null, null, null, null, null, null, null, null);
        unknownArea.Should().Throw<ArgumentException>();

        var entry = new AuditEntry(Now, AuditAreas.Store, "store.settings.updated", 5, 7, "TenantAdmin",
            "Tenant", new string('x', 150), null, "203.0.113.9", null);
        entry.TargetId.Should().HaveLength(AuditEntry.TargetIdMaxLength);
        entry.TenantId.Should().Be(5);
    }
}
