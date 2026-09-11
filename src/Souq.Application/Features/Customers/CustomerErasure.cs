using Souq.Application.Common.Security;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Customers;

// ============================================================================
// محو عميل (حقّ الحذف، المرحلة 7) — مسار واحد للعميل نفسه وللإدارة. الملف والحساب يُجرَّدان من البيانات الشخصية
// ويتوقّفان في حفظ واحد، ورموز التجديد تُلغى، وذاكرة ختم الجلسة تُنسى فتسقط توكنات الوصول القائمة فوراً.
// الطلبات والتقييمات تبقى (سجلّ مالي يُحتفَظ به) مشيرةً إلى ملف لم يعد يعرّف أحداً — لقطة عنوان الشحن على الطلب
// تبقى لأنها جزء من الفاتورة.
// ============================================================================
public sealed class CustomerErasure
{
    private readonly IUserRepository _users;
    private readonly IRefreshTokenRepository _tokens;
    private readonly IUnitOfWork _uow;
    private readonly ISessionValidator _sessions;
    private readonly TimeProvider _clock;

    public CustomerErasure(
        IUserRepository users, IRefreshTokenRepository tokens, IUnitOfWork uow, ISessionValidator sessions, TimeProvider clock)
    {
        _users = users; _tokens = tokens; _uow = uow; _sessions = sessions; _clock = clock;
    }

    public async Task EraseAsync(Customer customer, CancellationToken ct)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var user = await _users.GetByIdAsync(customer.UserId, ct);

        customer.Erase(now);
        if (user is not null)
        {
            user.Erase();
            foreach (var token in await _tokens.ListActiveForUserAsync(user.Id, ct))
                token.Revoke("Erased", now);
        }

        await _uow.SaveChangesAsync(ct);
        if (user is not null) _sessions.Forget(user.Id);
    }
}
