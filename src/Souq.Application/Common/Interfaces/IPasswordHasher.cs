namespace Souq.Application.Common.Interfaces;

// ============================================================================
// عقد تجزئة كلمة المرور. نُعرّفه في Application وننفّذه في Infrastructure (BCrypt)
// — نفس نمط IPaymentService. المنطق لا يعرف خوارزمية التجزئة؛ لتبديل BCrypt بـ
// Argon2 لاحقاً نكتب تنفيذاً جديداً فقط، دون لمس أوامر التسجيل/الدخول.
// ============================================================================
public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string passwordHash);
}
