using Souq.Domain.Common;

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

    private Customer() { }

    public Customer(string fullName, string email, string passwordHash, string role = "Customer")
    {
        FullName = fullName;
        Email = email;
        PasswordHash = passwordHash;
        Role = role;
    }
}
