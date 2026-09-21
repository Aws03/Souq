using AwesomeAssertions;
using Souq.Domain.Common;
using Souq.Domain.Entities;
using Souq.Domain.Exceptions;
using Souq.Domain.Identity;

namespace Souq.Domain.Tests;

// قواعد الحساب (ADR-0010): صيغة البريد، عالما المنصّة والمتجر، القفل، ختم الأمان، ورموز الاستخدام الواحد.
// الوقت يُمرَّر صراحةً (Phase 0 D12): لا DateTime.UtcNow داخل الكيان.
public class UserTests
{
    private static readonly DateTime Now = new(2026, 9, 11, 10, 0, 0, DateTimeKind.Utc);

    private static User NewUser(string role = Roles.Customer) => new("مستخدم", "  User@Souq.com ", "hashed", role);

    [Fact]
    public void البريد_يُطبَّع_للتخزين_وللبحث()
    {
        var user = NewUser();

        user.Email.Should().Be("user@souq.com");
        user.NormalizedEmail.Should().Be("USER@SOUQ.COM");
        user.Status.Should().Be(UserStatus.Active);
        user.SecurityStamp.Should().MatchRegex("^[0-9A-F]{32}$");
    }

    [Theory]
    [InlineData("مستخدم", "not-an-email", Roles.Customer)]
    [InlineData("", "a@b.com", Roles.Customer)]
    [InlineData("مستخدم", "a@b.com", "Admin")]
    public void بيانات_حساب_غير_صالحة_تُرفض(string name, string email, string role)
    {
        var act = () => new User(name, email, "hashed", role);

        act.Should().Throw<InvalidIdentityOperationException>();
    }

    [Theory]
    [InlineData(Roles.PlatformOwner, true)]
    [InlineData(Roles.PlatformAdmin, true)]
    [InlineData(Roles.TenantAdmin, false)]
    [InlineData(Roles.TenantStaff, false)]
    [InlineData(Roles.Customer, false)]
    public void الدور_يحدّد_عالم_الحساب(string role, bool platform)
    {
        NewUser(role).BelongsToPlatform.Should().Be(platform);
    }

    [Fact]
    public void لا_نقل_حساب_بين_المنصّة_والمتجر_بتغيير_الدور()
    {
        var staff = NewUser(Roles.TenantStaff);
        var stamp = staff.SecurityStamp;

        var toPlatform = () => staff.ChangeRole(Roles.PlatformAdmin);
        toPlatform.Should().Throw<InvalidIdentityOperationException>();

        staff.ChangeRole(Roles.TenantAdmin);
        staff.Role.Should().Be(Roles.TenantAdmin);
        staff.SecurityStamp.Should().NotBe(stamp, "تغيير الدور يُسقط التوكنات القديمة بصلاحياتها القديمة");
    }

    [Fact]
    public void خمس_محاولات_فاشلة_متتالية_تقفل_الحساب_مدّةً_ثم_ينفكّ()
    {
        var user = NewUser();

        for (var i = 0; i < User.MaxFailedLogins - 1; i++) user.RecordFailedLogin(Now);
        user.IsLockedOut(Now).Should().BeFalse();

        user.RecordFailedLogin(Now);
        user.IsLockedOut(Now).Should().BeTrue();
        user.IsLockedOut(Now.Add(User.LockoutDuration).AddSeconds(-1)).Should().BeTrue();
        user.IsLockedOut(Now.Add(User.LockoutDuration).AddSeconds(1)).Should().BeFalse();
    }

    [Fact]
    public void الدخول_الناجح_يصفّر_العدّاد_والقفل()
    {
        var user = NewUser();
        for (var i = 0; i < User.MaxFailedLogins - 1; i++) user.RecordFailedLogin(Now);

        user.RecordSuccessfulLogin(Now);
        for (var i = 0; i < User.MaxFailedLogins - 1; i++) user.RecordFailedLogin(Now);

        user.IsLockedOut(Now).Should().BeFalse();
        user.LastLoginAt.Should().Be(Now);
    }

    [Fact]
    public void رمز_إعادة_التعيين_تجزئته_فقط_تُخزَّن_وعشوائي_بطول_256_بت()
    {
        var user = NewUser();

        var first = user.GenerateResetToken(Now);
        var second = user.GenerateResetToken(Now);

        second.Should().NotBe(first);
        second.Should().HaveLength(43).And.MatchRegex("^[A-Za-z0-9_-]+$");
        user.PasswordResetTokenHash.Should().Be(User.HashToken(second)).And.NotBe(second);
        user.PasswordResetTokenExpiry.Should().Be(Now.AddHours(User.ResetTokenLifetimeHours));
        User.HashToken("sample").Should().MatchRegex("^[0-9A-F]{64}$").And.Be(User.HashToken("sample"));
    }

    [Fact]
    public void إعادة_التعيين_داخل_الصلاحية_تغيّر_الكلمة_وتمسح_الرمز_وتفكّ_القفل_وتُسقط_الجلسات()
    {
        var user = NewUser();
        for (var i = 0; i < User.MaxFailedLogins; i++) user.RecordFailedLogin(Now);
        user.GenerateResetToken(Now);
        var stamp = user.SecurityStamp;

        user.ResetPassword("new-hash", Now.AddHours(User.ResetTokenLifetimeHours).AddSeconds(-1));

        user.PasswordHash.Should().Be("new-hash");
        user.PasswordResetTokenHash.Should().BeNull();
        user.PasswordResetTokenExpiry.Should().BeNull();
        user.IsLockedOut(Now).Should().BeFalse();
        user.SecurityStamp.Should().NotBe(stamp);
    }

    [Fact]
    public void إعادة_التعيين_بعد_الصلاحية_أو_بلا_طلب_ترمي_ولا_تغيّر_شيئاً()
    {
        var expired = NewUser();
        expired.GenerateResetToken(Now);
        var withoutRequest = NewUser();

        var late = () => expired.ResetPassword("new-hash", Now.AddHours(User.ResetTokenLifetimeHours).AddSeconds(1));
        var none = () => withoutRequest.ResetPassword("new-hash", Now);

        late.Should().Throw<InvalidPasswordResetException>();
        none.Should().Throw<InvalidPasswordResetException>();
        expired.PasswordHash.Should().Be("hashed");
    }

    [Fact]
    public void تغيير_كلمة_المرور_والتعطيل_يدوّران_ختم_الأمان()
    {
        var user = NewUser();
        var original = user.SecurityStamp;

        user.ChangePassword("new-hash");
        var afterChange = user.SecurityStamp;
        user.Disable();

        afterChange.Should().NotBe(original);
        user.SecurityStamp.Should().NotBe(afterChange);
        user.Status.Should().Be(UserStatus.Disabled);
    }

    [Fact]
    public void ترقية_تجزئة_قديمة_لا_تُسقط_الجلسات()
    {
        var user = NewUser();
        var stamp = user.SecurityStamp;

        user.UpgradePasswordHash("$2a$11$upgraded");

        user.PasswordHash.Should().Be("$2a$11$upgraded");
        user.SecurityStamp.Should().Be(stamp);
    }

    [Fact]
    public void تأكيد_البريد_داخل_الصلاحية_مرّة_واحدة_وبعدها_يرمي()
    {
        var user = NewUser();
        var token = user.GenerateEmailVerificationToken(Now);
        user.EmailVerificationTokenHash.Should().Be(User.HashToken(token));

        user.ConfirmEmail(Now.AddHours(1));

        user.EmailConfirmedAt.Should().Be(Now.AddHours(1));
        user.EmailVerificationTokenHash.Should().BeNull();

        var expired = NewUser();
        expired.GenerateEmailVerificationToken(Now);
        var late = () => expired.ConfirmEmail(Now.AddHours(User.VerificationTokenLifetimeHours).AddSeconds(1));
        late.Should().Throw<InvalidEmailVerificationException>().Which.Code.Should().Be("VerificationTokenExpired");
    }
}

// ملف الشراء: يحتاج حساباً محفوظاً، ولا يحمل أي اعتماد دخول.
public class CustomerTests
{
    [Fact]
    public void ملف_العميل_يشير_لحساب_محفوظ_وبريد_تواصل_مُطبَّع()
    {
        var customer = new Customer(userId: 7, "  عميل  ", "Buyer@Souq.com");

        customer.UserId.Should().Be(7);
        customer.FullName.Should().Be("عميل");
        customer.Email.Should().Be("buyer@souq.com");
    }

    [Theory]
    [InlineData(0, "عميل", "a@b.com")]
    [InlineData(1, "", "a@b.com")]
    [InlineData(1, "عميل", "")]
    public void ملف_بلا_حساب_أو_اسم_أو_بريد_يُرفض(int userId, string name, string email)
    {
        var act = () => new Customer(userId, name, email);

        act.Should().Throw<InvalidIdentityOperationException>();
    }
}

// ============================================================================
// TD-63 — سياسات الأمان الرقمية، مثبَّتة **بأرقامها** لا بثوابتها.
//
// بقيّة اختبارات هذا الملفّ تقيس **الآليّة**: العدّ يقفل، والقفل ينتهي، والرمز يُستهلك مرّة.
// وهي تفعل ذلك بالرجوع إلى الثابت الذي يُعرّف السياسة — `User.MaxFailedLogins` — فتبقى خضراء
// مهما كانت قيمته. أي أنّ **تخفيف** السياسة لا يُفشل شيئاً: `MaxFailedLogins = 500` بناءٌ أخضر،
// والقفل ما زال "يعمل" وهو لم يعد يحرس شيئاً.
//
// وهذا يخصّ الأمان تحديداً: تشديدُ رقمٍ قرارٌ يُتخذ، وتخفيفُه — أو ضياعُه في دمج — لا يُلاحظه
// أحد. فالأرقام المعلَنة تُقال هنا حرفياً، كما تفعل `PasswordRulesTests.الحدّان_هما_ما_تُعلنه_الواجهة`
// لحدَّي كلمة المرور أصلاً.
//
// يوم تتغيّر سياسة بقرار، يحمرّ هذا الاختبار ويُحدَّث سطره — وذلك بالضبط هو الفرق بين قرارٍ
// وانزلاق.
// ============================================================================
public class SecurityPolicyNumbersTests
{
    [Fact]
    public void حدّ_محاولات_الدخول_ومدّة_القفل_هما_المعلنان()
    {
        // خمس محاولات ثم ربع ساعة: مكتوبة في Security.md وفي رسالة القفل التي يراها المستخدم.
        User.MaxFailedLogins.Should().Be(5, "تخفيف الحدّ قرارٌ يُتخذ، لا تغييرٌ يمرّ");
        User.LockoutDuration.Should().Be(TimeSpan.FromMinutes(15));
    }

    [Fact]
    public void أعمار_الرموز_ذات_الاستخدام_الواحد_هي_المعلنة()
    {
        // ساعتان لإعادة التعيين: أقصر الثلاثة عمداً — وهو الرمز الذي يُرسَل إلى بريدٍ قد يكون
        // هو نفسه المُخترَق. والتأكيد والدعوة أطول لأنهما لا يمنحان تحكّماً بحسابٍ قائم.
        User.ResetTokenLifetimeHours.Should().Be(2);
        User.VerificationTokenLifetimeHours.Should().Be(48);
        User.InvitationTokenLifetimeHours.Should().Be(72);
    }

    [Fact]
    public void مهلة_إعادة_إرسال_رابط_التعيين_هي_المعلنة()
    {
        // دقيقتان: تمنع إغراق بريد شخص بروابط إعادة تعيين، وتبقى قصيرة بما يكفي لمن لم تصله الأولى.
        User.ResetResendCooldownMinutes.Should().Be(2);
    }

    [Fact]
    public void الأعمار_مرتّبة_بما_يعكس_خطر_كل_رمز()
    {
        // ليست حسابية بل قاعدة: رمزٌ يُعيد ضبط كلمة مرور حسابٍ قائم أخطر من رمزٍ يؤكّد بريداً،
        // وأخطر من دعوةٍ لحسابٍ لم يوجد بعد. لو انعكس الترتيب يوماً فذلك خطأ لا تفضيل.
        User.ResetTokenLifetimeHours.Should().BeLessThan(User.VerificationTokenLifetimeHours);
        User.VerificationTokenLifetimeHours.Should().BeLessThan(User.InvitationTokenLifetimeHours);
    }
}
