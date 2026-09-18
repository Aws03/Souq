using MediatR;
using Souq.Application.Common.Models;
using Souq.Application.Common.Security;
using Souq.Domain.Interfaces;
using Souq.Application.Features.Auth.Contracts;

namespace Souq.Application.Features.Auth.Queries;

// الحساب الحالي كما هو في القاعدة الآن (لا كما كان وقت إصدار التوكن): الاسم، الدور، الصلاحيات،
// حالة تأكيد البريد، وملف العميل إن وُجد.
public record GetCurrentUserQuery : IRequest<Result<UserInfo>>;

public class GetCurrentUserHandler : IRequestHandler<GetCurrentUserQuery, Result<UserInfo>>
{
    private readonly IUserRepository _users;
    private readonly IAccountProfiles _profiles;
    private readonly ICurrentUser _currentUser;

    public GetCurrentUserHandler(IUserRepository users, IAccountProfiles profiles, ICurrentUser currentUser)
    {
        _users = users; _profiles = profiles; _currentUser = currentUser;
    }

    public async Task<Result<UserInfo>> Handle(GetCurrentUserQuery q, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(_currentUser.RequireUserId(), ct);
        if (user is null)
            return Result<UserInfo>.Failure(Error.Unauthorized("Unauthenticated", "سجّل الدخول للمتابعة."));

        var customerId = user.BelongsToPlatform ? null : await _profiles.FindIdForAccountAsync(user.Id, ct);
        return Result<UserInfo>.Success(UserInfo.From(user, customerId));
    }
}
