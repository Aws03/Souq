using Souq.Domain.Common;
using Souq.Domain.Exceptions;

namespace Souq.Domain.Entities;

// ============================================================================
// Customer — العميل. نخزّن PasswordHash لا كلمة المرور نفسها أبداً.
// (مبدأ غير وظيفي: الأمان. كلمة المرور الخام لا تُخزّن في أي نظام محترم.)
// ============================================================================
public class Customer : Entity
{
    public string FullName { get; private set; } = default!;
    public string Email { get; private set; } = default!;
    public string PasswordHash { get; private set; } = default!;
    public string Role { get; private set; } = "Customer";   // Customer أو Admin

    // رمز إعادة تعيين كلمة المرور — استخدام واحد (Single-use)، يُمسح فور نجاح
    // إعادة التعيين. NULL يعني: لا طلب إعادة تعيين قائم حالياً.
    public string? PasswordResetToken { get; private set; }
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

    // يولّد رمزاً عشوائياً صالحاً لساعتين ويُعيده — النسخة الوحيدة الواضحة منه؛
    // لا نُعيد قراءته لاحقاً كنص صريح (يُقارَن فقط عبر استعلام المستودع).
    public string GenerateResetToken()
    {
        var token = Guid.NewGuid().ToString("N");
        PasswordResetToken = token;
        PasswordResetTokenExpiry = DateTime.UtcNow.AddHours(2);
        return token;
    }

    // يُستدعى بعد أن يجد المستودع العميل عبر الرمز نفسه (تطابق الرمز تحقّق عنه
    // الاستعلام مسبقاً) — مسؤولية الكيان هنا: التأكد أن الرمز لم تنتهِ صلاحيته
    // فقط، ثم تعيين كلمة المرور الجديدة ومسح حقول الرمز فوراً (استخدام واحد).
    public void ResetPassword(string newPasswordHash)
    {
        if (PasswordResetTokenExpiry is null || PasswordResetTokenExpiry < DateTime.UtcNow)
            throw new InvalidPasswordResetException("انتهت صلاحية رابط إعادة التعيين. اطلب رابطاً جديداً.");

        PasswordHash = newPasswordHash;
        PasswordResetToken = null;
        PasswordResetTokenExpiry = null;
    }
}
