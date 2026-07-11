using Souq.Application.Common.Interfaces;

namespace Souq.Infrastructure.Services;

// ============================================================================
// تنفيذ التجزئة بـ BCrypt: بطيء عمداً ويولّد ملحاً (salt) داخلياً لكل كلمة مرور،
// فيقاوم هجمات جداول القوس قزح والقوة الغاشمة. المنطق في Application لا يعرف هذا.
// ============================================================================
public class BcryptPasswordHasher : IPasswordHasher
{
    public string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password);

    public bool Verify(string password, string passwordHash)
    {
        // تجزئة تالفة أو بصيغة غير BCrypt (مثل بذرة "HASHED_..." القديمة) يجب أن
        // تُعامَل كفشل تحقّق لا كخطأ خادم يُسقط الطلب.
        try { return BCrypt.Net.BCrypt.Verify(password, passwordHash); }
        catch (BCrypt.Net.SaltParseException) { return false; }
    }
}
