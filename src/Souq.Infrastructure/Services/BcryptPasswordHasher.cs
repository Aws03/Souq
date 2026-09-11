using Souq.Application.Common.Interfaces;

namespace Souq.Infrastructure.Services;

// ============================================================================
// تنفيذ التجزئة بـ BCrypt: بطيء عمداً ويولّد ملحاً (salt) داخلياً لكل كلمة مرور،
// فيقاوم هجمات جداول القوس قزح والقوة الغاشمة. المنطق في Application لا يعرف هذا.
// ============================================================================
public class BcryptPasswordHasher : IPasswordHasher
{
    // تجزئة حقيقية لقيمة عشوائية تُولَّد مرة: التحقّق مقابلها يستغرق زمن BCrypt نفسه.
    private static readonly Lazy<string> TimingDecoy =
        new(() => BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N")));

    public string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password);

    public bool Verify(string password, string passwordHash)
    {
        // تجزئة فارغة (حساب غير موجود) ⇒ تحقّق صوري بالزمن نفسه ثم false: دخول ببريد غير مسجّل
        // يستغرق زمن كلمة مرور خاطئة تماماً، فلا يُكشف وجود الحساب من زمن الاستجابة.
        if (string.IsNullOrEmpty(passwordHash))
        {
            BCrypt.Net.BCrypt.Verify(password, TimingDecoy.Value);
            return false;
        }

        // تجزئة تالفة أو بصيغة غير BCrypt (مثل بذرة "HASHED_..." القديمة) يجب أن
        // تُعامَل كفشل تحقّق لا كخطأ خادم يُسقط الطلب.
        try { return BCrypt.Net.BCrypt.Verify(password, passwordHash); }
        catch (BCrypt.Net.SaltParseException) { return false; }
    }
}
