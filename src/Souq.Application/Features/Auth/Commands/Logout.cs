using MediatR;
using Souq.Application.Common.Models;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Auth.Commands;

// تسجيل الخروج يُبطل عائلة الجلسة كلها (هذا الجهاز). ناجح دائماً — حتى بلا رمز أو برمز مجهول — كي
// تستطيع الواجهة التنظيف بلا حالة خطأ. توكن الوصول الحالي يسقط خلال دقائق من تلقاء نفسه.
public record LogoutCommand(string? RefreshToken) : IRequest<Result>;

public class LogoutHandler : IRequestHandler<LogoutCommand, Result>
{
    private readonly IRefreshTokenRepository _tokens;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public LogoutHandler(IRefreshTokenRepository tokens, IUnitOfWork uow, TimeProvider clock)
    {
        _tokens = tokens; _uow = uow; _clock = clock;
    }

    public async Task<Result> Handle(LogoutCommand cmd, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cmd.RefreshToken)) return Result.Success();

        var token = await _tokens.GetByTokenAsync(cmd.RefreshToken, ct);
        if (token is null) return Result.Success();

        var now = _clock.GetUtcNow().UtcDateTime;
        foreach (var sibling in await _tokens.ListActiveInFamilyAsync(token.FamilyId, ct))
            sibling.Revoke("Logout", now);
        token.Revoke("Logout", now);
        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
