using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Souq.Domain.Common;
using Souq.Domain.Exceptions;

namespace Souq.Domain.Identity;

// ============================================================================
// User — من يستطيع الدخول (وحدة Identity، ADR-0010، D-06). منفصل عن Customer (ملف الشراء) كي
// يكون لمدير المتجر وموظّفه ومالك المنصّة حساب دخول بلا ملف عميل، وكي تبقى قواعد الأمان في مكان واحد:
//   • البريد فريد داخل المتجر (NormalizedEmail)؛ حساب المنصّة بلا متجر (TenantId = null).
//   • القفل بعد محاولات فاشلة متتالية، ويُفكّ بمرور المدّة أو بإعادة تعيين كلمة المرور.
//   • ختم الأمان (SecurityStamp) يتغيّر مع كل ما يُبطل الجلسات: كلمة مرور، دور، تعطيل، إعادة تعيين،
//     أو اكتشاف إعادة استخدام رمز تجديد — والتوكنات تحمله فتسقط فوراً.
//   • رموز إعادة التعيين والتحقّق من البريد: 256 بت عشوائية، تُخزَّن تجزئتها فقط، استخدام واحد.
// ============================================================================
public class User : Entity, ITenantOrPlatformOwned
{
    public const int MaxFailedLogins = 5;
    public const int ResetTokenLifetimeHours = 2;
    public const int VerificationTokenLifetimeHours = 48;
    public const int FullNameMaxLength = 150;
    public const int EmailMaxLength = 256;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public int? TenantId { get; private set; }
    public string Email { get; private set; } = default!;
    public string NormalizedEmail { get; private set; } = default!;
    public string FullName { get; private set; } = default!;
    public string PasswordHash { get; private set; } = default!;
    public string Role { get; private set; } = default!;
    public UserStatus Status { get; private set; }
    public string SecurityStamp { get; private set; } = default!;
    public int FailedLoginCount { get; private set; }
    public DateTime? LockoutEndsAt { get; private set; }
    public DateTime? EmailConfirmedAt { get; private set; }
    public DateTime? LastLoginAt { get; private set; }
    public string? PasswordResetTokenHash { get; private set; }
    public DateTime? PasswordResetTokenExpiry { get; private set; }
    public string? EmailVerificationTokenHash { get; private set; }
    public DateTime? EmailVerificationTokenExpiry { get; private set; }

    public bool BelongsToPlatform => Roles.IsPlatform(Role);

    private User() { }

    // حساب متجر (يختم حارس الكتابة TenantId من النطاق) أو حساب منصّة (دور منصّة، يبقى بلا متجر).
    public User(string fullName, string email, string passwordHash, string role)
    {
        Rename(fullName);
        SetEmail(email);
        SetPasswordHash(passwordHash);
        Role = ValidRole(role);
        Status = UserStatus.Active;
        RotateSecurityStamp();
    }

    public static string NormalizeEmail(string email) => email.Trim().ToUpperInvariant();

    public bool IsLockedOut(DateTime utcNow) => LockoutEndsAt is { } end && end > utcNow;

    // الفشل الخامس المتتالي يقفل الحساب مدّةً ثابتة ويصفّر العدّاد (القفل التالي يحتاج خمساً جديدة).
    public void RecordFailedLogin(DateTime utcNow)
    {
        FailedLoginCount++;
        if (FailedLoginCount < MaxFailedLogins) return;
        LockoutEndsAt = utcNow.Add(LockoutDuration);
        FailedLoginCount = 0;
    }

    public void RecordSuccessfulLogin(DateTime utcNow)
    {
        FailedLoginCount = 0;
        LockoutEndsAt = null;
        LastLoginAt = utcNow;
    }

    // تغيير كلمة المرور (والمستخدم مسجّل) يُبطل كل الجلسات الأخرى.
    public void ChangePassword(string newPasswordHash)
    {
        SetPasswordHash(newPasswordHash);
        ClearResetToken();
        RotateSecurityStamp();
    }

    // ترقية تجزئة قديمة (غير BCrypt) بلا إبطال الجلسات — لا تغيّر كلمة المرور نفسها.
    public void UpgradePasswordHash(string newPasswordHash) => SetPasswordHash(newPasswordHash);

    public string GenerateResetToken(DateTime utcNow)
    {
        var token = NewToken();
        PasswordResetTokenHash = HashToken(token);
        PasswordResetTokenExpiry = utcNow.AddHours(ResetTokenLifetimeHours);
        return token;
    }

    // صلاحية الرمز يحرسها الكيان؛ المطابقة نفسها بحث المستودع بالتجزئة. نجاحها يفكّ القفل أيضاً
    // (من يملك بريده يستعيد حسابه) ويُبطل كل الجلسات القائمة.
    public void ResetPassword(string newPasswordHash, DateTime utcNow)
    {
        if (PasswordResetTokenExpiry is null || PasswordResetTokenExpiry < utcNow)
            throw new InvalidPasswordResetException("انتهت صلاحية رابط إعادة التعيين. اطلب رابطاً جديداً.");

        SetPasswordHash(newPasswordHash);
        ClearResetToken();
        FailedLoginCount = 0;
        LockoutEndsAt = null;
        RotateSecurityStamp();
    }

    public string GenerateEmailVerificationToken(DateTime utcNow)
    {
        var token = NewToken();
        EmailVerificationTokenHash = HashToken(token);
        EmailVerificationTokenExpiry = utcNow.AddHours(VerificationTokenLifetimeHours);
        return token;
    }

    public void ConfirmEmail(DateTime utcNow)
    {
        if (EmailVerificationTokenExpiry is null || EmailVerificationTokenExpiry < utcNow)
            throw new InvalidEmailVerificationException("انتهت صلاحية رابط تأكيد البريد. اطلب رابطاً جديداً.");

        EmailConfirmedAt ??= utcNow;
        EmailVerificationTokenHash = null;
        EmailVerificationTokenExpiry = null;
    }

    public void Disable()
    {
        Status = UserStatus.Disabled;
        RotateSecurityStamp();
    }

    public void Enable() => Status = UserStatus.Active;

    // لا عبور بين عالمَي المنصّة والمتجر بتغيير الدور: حساب المتجر يبقى حساب متجر.
    public void ChangeRole(string role)
    {
        var valid = ValidRole(role);
        if (Roles.IsPlatform(valid) != BelongsToPlatform)
            throw new InvalidIdentityOperationException("لا يمكن نقل حساب بين المنصّة والمتجر بتغيير الدور");
        if (valid == Role) return;
        Role = valid;
        RotateSecurityStamp();
    }

    public void Rename(string fullName)
    {
        var trimmed = fullName?.Trim() ?? "";
        if (trimmed.Length is 0 or > FullNameMaxLength)
            throw new InvalidIdentityOperationException($"الاسم مطلوب (حتى {FullNameMaxLength} حرفاً)");
        FullName = trimmed;
    }

    public void RotateSecurityStamp() => SecurityStamp = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    // SHA-256 كافٍ (لا ملح ولا خوارزمية بطيئة): الرمز عشوائي بعرض 256 بت، فلا قاموس ولا تخمين
    // ممكن — بخلاف كلمات المرور التي يختارها البشر (لها BCrypt).
    public static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    internal static string NewToken() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    private void SetEmail(string email)
    {
        var trimmed = email?.Trim() ?? "";
        if (trimmed.Length is 0 or > EmailMaxLength || !trimmed.Contains('@'))
            throw new InvalidIdentityOperationException("بريد إلكتروني غير صالح");
        Email = trimmed.ToLowerInvariant();
        NormalizedEmail = NormalizeEmail(trimmed);
    }

    private void SetPasswordHash(string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new ArgumentException("تجزئة كلمة المرور مطلوبة", nameof(passwordHash));
        PasswordHash = passwordHash;
    }

    private void ClearResetToken()
    {
        PasswordResetTokenHash = null;
        PasswordResetTokenExpiry = null;
    }

    private static string ValidRole(string role) =>
        Roles.All.Contains(role) ? role : throw new InvalidIdentityOperationException($"دور غير معروف: {role}");
}
