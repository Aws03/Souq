namespace Souq.Domain.Identity;

// حالة الحساب: Disabled يمنع الدخول والتجديد فوراً (يُدوَّر ختم الأمان عند التعطيل فتسقط الجلسات).
public enum UserStatus
{
    Active = 0,
    Disabled = 1,
}
