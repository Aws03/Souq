using FluentValidation;

namespace Souq.Application.Features.Auth;

// سياسة كلمة المرور في مكان واحد لكل مداخلها (تسجيل، تغيير، إعادة تعيين): 8–128 حرفاً، حروف وأرقام
// معاً. الطول هو الحماية الأهم (NIST 800-63B)؛ الحدّ الأعلى يمنع حرمان خدمة رخيصاً عبر BCrypt.
public static class PasswordRules
{
    public const int MinLength = 8;
    public const int MaxLength = 128;

    public static IRuleBuilderOptions<T, string> StrongPassword<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty()
            .MinimumLength(MinLength)
            .MaximumLength(MaxLength)
            .Must(p => p is not null && p.Any(char.IsLetter) && p.Any(char.IsDigit))
            .WithMessage("كلمة المرور يجب أن تحتوي حروفاً وأرقاماً معاً");
}
