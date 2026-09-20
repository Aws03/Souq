using AwesomeAssertions;
using NSubstitute;
using Souq.Application.Common.Interfaces;
using Souq.Application.Features.Auth;

namespace Souq.Application.Tests.Auth;

// ============================================================================
// "مرّة لكل تجزئة لا مرّة لكل محاولة" ادّعاءٌ يُبنى عليه سقفُ المحاولات في `AccountWriter`
// (ستّ عشرة)، فيجب أن يُقاس لا أن يُفترض: لو تسلّل استدعاءٌ ثانٍ لـ BCrypt في كل إعادة
// لصار ذلك السقفُ مضخّةَ إنهاك بدل أن يكون تصريفاً للازدحام.
// ============================================================================
public class PasswordCheckTests
{
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();

    [Fact]
    public void التجزئة_نفسها_تُحسب_مرّة_مهما_تكرّرت_المحاولات()
    {
        _hasher.Verify("كلمتي", "تجزئة").Returns(true);
        var check = new PasswordCheck(_hasher, "كلمتي");

        for (var attempt = 0; attempt < AccountWriter.MaxAttempts; attempt++)
            check.Matches("تجزئة").Should().BeTrue();

        _hasher.Received(1).Verify("كلمتي", "تجزئة");
    }

    [Fact]
    public void الرفض_يُحفظ_كما_يُحفظ_القبول()
    {
        _hasher.Verify("خطأ", "تجزئة").Returns(false);
        var check = new PasswordCheck(_hasher, "خطأ");

        check.Matches("تجزئة").Should().BeFalse();
        check.Matches("تجزئة").Should().BeFalse();

        // مسار كلمة المرور الخاطئة يكتب عدّاد الإخفاق، فيتسابق ويُعاد هو الآخر.
        _hasher.Received(1).Verify("خطأ", "تجزئة");
    }

    [Fact]
    public void تجزئةٌ_تغيّرت_تُبطل_المحفوظ_ويُعاد_الحساب_عليها()
    {
        _hasher.Verify("كلمتي", "القديمة").Returns(true);
        _hasher.Verify("كلمتي", "الجديدة").Returns(false);
        var check = new PasswordCheck(_hasher, "كلمتي");

        check.Matches("القديمة").Should().BeTrue();

        // هذا هو الشرط الأمني: طلبٌ متزامن غيّر كلمة المرور وفاز بالسباق، فالمحاولة التالية
        // تقرأ التجزئة الجديدة — ويجب أن تُرفض، لا أن تُصدر جلسة بقرارٍ حُسب على تجزئة ماتت.
        check.Matches("الجديدة").Should().BeFalse();
        _hasher.Received(1).Verify("كلمتي", "الجديدة");

        // ورجوعٌ إلى القديمة يُحسب من جديد: المحفوظ واحدٌ بعينه لا جدولُ إجابات.
        check.Matches("القديمة").Should().BeTrue();
        _hasher.Received(2).Verify("كلمتي", "القديمة");
    }
}
