using Souq.Domain.Entities;

namespace Souq.Domain.Interfaces;

public interface ICustomerRepository : IRepository<Customer>
{
    // البريد هو هوية الدخول — الاستعلام الأساسي في التسجيل وتسجيل الدخول.
    Task<Customer?> GetByEmailAsync(string email, CancellationToken ct = default);

    // يبحث عن العميل عبر رمز إعادة تعيين كلمة المرور — تطابق الرمز نفسه هو
    // التحقّق الأول (ثم صلاحية الانتهاء يحرسها Customer.ResetPassword).
    Task<Customer?> GetByResetTokenAsync(string token, CancellationToken ct = default);
}
