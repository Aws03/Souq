using Souq.Domain.Common;
using Souq.Domain.Exceptions;

namespace Souq.Domain.Entities;

// ============================================================================
// Customer — ملف الشراء لحساب داخل متجر واحد (وحدة Customers، D-06). منذ المرحلة 3 لا يحمل أي
// اعتماد دخول: البريد وكلمة المرور والدور والرموز انتقلت إلى User (Identity)، وهذا الملف يشير إليه
// بـ UserId. الطلبات والتقييمات تشير إلى Customer.Id كما كانت (المعرّفات القديمة ثابتة).
// Email هنا بريد التواصل (رسائل الطلبات) — نسخة من بريد الحساب عند التسجيل، لا هوية دخول.
// ============================================================================
public class Customer : Entity, ITenantOwned
{
    public const int FullNameMaxLength = 150;

    public int TenantId { get; private set; }
    public int UserId { get; private set; }
    public string FullName { get; private set; } = default!;
    public string Email { get; private set; } = default!;

    private Customer() { }

    public Customer(int userId, string fullName, string email)
    {
        if (userId <= 0)
            throw new InvalidIdentityOperationException("ملف العميل يحتاج حساب دخول محفوظاً");
        UserId = userId;
        Rename(fullName);
        Email = string.IsNullOrWhiteSpace(email)
            ? throw new InvalidIdentityOperationException("بريد التواصل مطلوب")
            : email.Trim().ToLowerInvariant();
    }

    public void Rename(string fullName)
    {
        var trimmed = fullName?.Trim() ?? "";
        if (trimmed.Length is 0 or > FullNameMaxLength)
            throw new InvalidIdentityOperationException($"الاسم مطلوب (حتى {FullNameMaxLength} حرفاً)");
        FullName = trimmed;
    }
}
