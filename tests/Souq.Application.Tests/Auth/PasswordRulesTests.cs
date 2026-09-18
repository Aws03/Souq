using AwesomeAssertions;
using Souq.Application.Features.Auth;
using Souq.Application.Features.Auth.Commands;

namespace Souq.Application.Tests.Auth;

// ============================================================================
// سياسة كلمة المرور — لم يكن لها **أي** اختبار قبل M9، وهي القاعدة الوحيدة التي تحرس قوّة بيانات
// اعتماد كل حساب في النظام: العميل، وموظّف المتجر، ومالك المنصّة. كلّهم يمرّون بـ StrongPassword.
//
// وأهمّ من التغطية في ذاتها: **الواجهة تُكرّر هذه القواعد** الآن
// (`frontend/src/features/account/passwordForm.js`) كي تقولها قبل الإرسال بدل أن يمشي المستخدم
// رحلةً كاملة ليقرأ رفضاً. لا شيء يربط الملفَّين، فهذا الاختبار هو الموضع الذي يُكتب فيه العقد صراحةً:
// إن ضاقت القاعدة هنا ولم تضق هناك، أُرسل ما يُرفض؛ وإن اتّسعت هنا ولم تتّسع هناك، مُنع ما يُقبَل.
//
// ولهذا تُختبر الحروف والأرقام **غير اللاتينية** تحديداً: `char.IsLetter` و`char.IsDigit` يعملان على
// فئات Unicode لا على ASCII، فكلمة مرور عربية بأرقام عربية-هندية مقبولة. لو كان الظنّ غير ذلك لكانت
// الواجهة (التي تفحص `\p{L}` و`\p{Nd}`) أوسع من الخادم — وهو الخطأ الذي يُرسل المرفوض.
// ============================================================================
public class PasswordRulesTests
{
    private readonly ChangePasswordValidator _validator = new();

    private static bool Accepts(string newPassword, string currentPassword = "current-passw0rd") =>
        new ChangePasswordValidator()
            .Validate(new ChangePasswordCommand(currentPassword, newPassword)).IsValid;

    [Theory]
    [InlineData("passw0rd")]                         // الحدّ الأدنى بالضبط: ثمانية، بحرف ورقم
    [InlineData("Checkout@12345")]                   // الرموز مسموحة، وغير مطلوبة
    [InlineData("كلمةسر123")]                        // حروف عربية وأرقام لاتينية
    [InlineData("كلمةسر١٢٣")]                        // حروف عربية و**أرقام عربية-هندية**: char.IsDigit فئةُ Unicode
    [InlineData("passwörd1")]                        // حروف بعلامات
    public void كلمة_قوية_مقبولة(string password) => Accepts(password).Should().BeTrue();

    [Theory]
    [InlineData("")]                                 // فارغة
    [InlineData("pass0")]                            // أقصر من ثمانية
    [InlineData("aaaaaaaa")]                         // ثمانية بلا رقم
    [InlineData("12345678")]                         // ثمانية بلا حرف
    [InlineData("١٢٣٤٥٦٧٨")]                         // أرقام عربية-هندية بلا حرف — ترفض كما ترفض نظيرتها
    [InlineData("!@#$%^&*")]                         // رموز وحدها: لا حرف ولا رقم
    public void كلمة_ضعيفة_مرفوضة(string password) => Accepts(password).Should().BeFalse();

    [Fact]
    public void الحدّ_الأعلى_مئة_وثمانية_وعشرون_ولا_حرف_بعده()
    {
        Accepts("a1" + new string('x', PasswordRules.MaxLength - 2)).Should().BeTrue("الحدّ الأعلى نفسه مقبول");
        Accepts("a1" + new string('x', PasswordRules.MaxLength - 1)).Should().BeFalse("حرف واحد بعده مرفوض");
    }

    [Fact]
    public void الحدّان_هما_ما_تُعلنه_الواجهة()
    {
        // الأرقام مكتوبة في نصوص الواجهة ("ثمانية أحرف على الأقل") وفي passwordForm.js. تُقال هنا كي
        // يفشل هذا الاختبار — لا شاشةُ مستخدم — إن غُيِّرت السياسة بلا تحديث الواجهة معها.
        (PasswordRules.MinLength, PasswordRules.MaxLength).Should().Be((8, 128));
    }

    [Fact]
    public void الجديدة_المساوية_للحالية_مرفوضة_وإن_كانت_قوية()
    {
        Accepts("strong-passw0rd", currentPassword: "strong-passw0rd").Should().BeFalse();
        Accepts("strong-passw0rd", currentPassword: "other-passw0rd").Should().BeTrue();
    }

    [Fact]
    public void الحالية_مطلوبة_ولها_سقف_طول()
    {
        _validator.Validate(new ChangePasswordCommand("", "new-passw0rd")).IsValid.Should().BeFalse();
        // سقف على الحالية أيضاً: لا تُمرَّر سلسلة ضخمة إلى دالّة التجزئة قبل أن تُرفض.
        _validator.Validate(new ChangePasswordCommand(new string('x', PasswordRules.MaxLength + 1), "new-passw0rd"))
            .IsValid.Should().BeFalse();
    }
}
