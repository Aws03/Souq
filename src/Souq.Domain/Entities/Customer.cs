using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Souq.Domain.Common;
using Souq.Domain.Exceptions;

namespace Souq.Domain.Entities;

// ============================================================================
// Customer — العميل. نخزّن PasswordHash لا كلمة المرور نفسها أبداً.
// (مبدأ غير وظيفي: الأمان. كلمة المرور الخام لا تُخزّن في أي نظام محترم.)
// ============================================================================
public class Customer : Entity
{
    public const int ResetTokenLifetimeHours = 2;

    public string FullName { get; private set; } = default!;
    public string Email { get; private set; } = default!;
    public string PasswordHash { get; private set; } = default!;
    public string Role { get; private set; } = "Customer";   // Customer أو Admin

    // تجزئة SHA-256 لرمز إعادة التعيين — لا الرمز نفسه أبداً: من يقرأ قاعدة البيانات
    // لا يحصل على رابط صالح. الرمز الخام يُعاد مرة واحدة فقط ليُرسَل بالبريد.
    // NULL يعني: لا طلب إعادة تعيين قائم حالياً.
    public string? PasswordResetTokenHash { get; private set; }
    public DateTime? PasswordResetTokenExpiry { get; private set; }

    private Customer() { }

    public Customer(string fullName, string email, string passwordHash, string role = "Customer")
    {
        FullName = fullName;
        Email = email;
        PasswordHash = passwordHash;
        Role = role;
    }

    // الباب الوحيد لتغيير التجزئة (إعادة تعيين كلمة مرور، ترقية hash قديم).
    public void ChangePasswordHash(string newPasswordHash)
    {
        if (string.IsNullOrWhiteSpace(newPasswordHash))
            throw new ArgumentException("تجزئة كلمة المرور مطلوبة", nameof(newPasswordHash));
        PasswordHash = newPasswordHash;
    }

    // رمز من 32 بايت عشوائية آمنة تشفيرياً (CSPRNG) بترميز base64url صالح للروابط.
    // نخزّن تجزئته فقط ونُعيد الخام للمستدعي — النسخة الوحيدة الواضحة منه.
    // utcNow يمرّره المستدعي من TimeProvider (لا DateTime.UtcNow هنا): الكيان حتمي، وتُختبر
    // الصلاحية بساعة ثابتة بلا انتظار ولا Reflection (Phase 0 D12).
    public string GenerateResetToken(DateTime utcNow)
    {
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        PasswordResetTokenHash = HashResetToken(token);
        PasswordResetTokenExpiry = utcNow.AddHours(ResetTokenLifetimeHours);
        return token;
    }

    // SHA-256 كافٍ هنا (لا ملح ولا خوارزمية بطيئة): الرمز عشوائي بعرض 256 بت، فلا
    // قاموس ولا تخمين ممكن — بخلاف كلمات المرور التي يختارها البشر (لها BCrypt).
    public static string HashResetToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    // يُستدعى بعد أن يجد المستودع العميل عبر تجزئة الرمز — مسؤولية الكيان هنا: التأكد
    // أن الرمز لم تنتهِ صلاحيته، ثم تعيين كلمة المرور الجديدة ومسح الرمز فوراً (استخدام واحد).
    public void ResetPassword(string newPasswordHash, DateTime utcNow)
    {
        if (PasswordResetTokenExpiry is null || PasswordResetTokenExpiry < utcNow)
            throw new InvalidPasswordResetException("انتهت صلاحية رابط إعادة التعيين. اطلب رابطاً جديداً.");

        PasswordHash = newPasswordHash;
        PasswordResetTokenHash = null;
        PasswordResetTokenExpiry = null;
    }
}
