using Souq.Domain.Identity;

namespace Souq.Domain.Interfaces;

// منفذ الكتابة لحسابات الدخول. كل بحث مُرشَّح بالنطاق: حسابات هذا المتجر على مضيفه، وحسابات
// المنصّة على مضيف المنصّة — فالبريد نفسه في متجرين حسابان لا يلتقيان أبداً.
public interface IUserRepository : IRepository<User>
{
    Task<User?> GetByEmailAsync(string email, CancellationToken ct = default);

    // الرمز الخام يُجزَّأ بقاعدة الكيان نفسها ويُبحث بالتجزئة (القاعدة لا تحمل رمزاً صالحاً).
    Task<User?> GetByResetTokenAsync(string token, CancellationToken ct = default);

    Task<User?> GetByVerificationTokenAsync(string token, CancellationToken ct = default);

    // الحسابات القادرة فعلاً على الدخول بهذا الدور في النطاق (فعّالة وقبلت دعوتها) — حارس "آخر مدير".
    Task<int> CountActiveByRoleAsync(string role, CancellationToken ct = default);

    // معرّفات الحسابات القادرة على الدخول بأحد هذه الأدوار في النطاق — مستلمو إشعارات الإدارة (المرحلة 14).
    Task<IReadOnlyList<int>> ListActiveIdsByRolesAsync(IReadOnlyCollection<string> roles, CancellationToken ct = default);
}

// منفذ رموز التجديد: بحث بالتجزئة، وإبطال عائلة أو كل جلسات مستخدم (تغيير كلمة المرور، سرقة).
public interface IRefreshTokenRepository
{
    Task AddAsync(RefreshToken token, CancellationToken ct = default);

    Task<RefreshToken?> GetByTokenAsync(string token, CancellationToken ct = default);

    Task<IReadOnlyList<RefreshToken>> ListActiveInFamilyAsync(Guid familyId, CancellationToken ct = default);

    Task<IReadOnlyList<RefreshToken>> ListActiveForUserAsync(int userId, CancellationToken ct = default);
}
