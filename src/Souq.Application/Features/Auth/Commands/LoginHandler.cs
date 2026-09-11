using MediatR;
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Models;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Auth.Commands;

public class LoginHandler : IRequestHandler<LoginCommand, Result<AuthResponse>>
{
    private readonly ICustomerRepository _customers;
    private readonly IPasswordHasher _hasher;
    private readonly IJwtTokenGenerator _jwt;

    public LoginHandler(ICustomerRepository customers, IPasswordHasher hasher, IJwtTokenGenerator jwt)
    {
        _customers = customers; _hasher = hasher; _jwt = jwt;
    }

    public async Task<Result<AuthResponse>> Handle(LoginCommand cmd, CancellationToken ct)
    {
        var customer = await _customers.GetByEmailAsync(cmd.Email.Trim().ToLowerInvariant(), ct);

        // رسالة موحّدة سواء كان البريد غير مسجّل أو كلمة المرور خاطئة — كي لا نكشف
        // أي البريدين مسجّل (منع تعداد الحسابات). Verify يُشغَّل دائماً منطقياً.
        if (customer is null || !_hasher.Verify(cmd.Password, customer.PasswordHash))
            return Result<AuthResponse>.Failure(
                Error.Unauthorized("InvalidCredentials", "البريد الإلكتروني أو كلمة المرور غير صحيحة"));

        var (token, expires) = _jwt.Generate(customer);
        return Result<AuthResponse>.Success(new AuthResponse(token, expires,
            new UserInfo(customer.Id, customer.FullName, customer.Email, customer.Role)));
    }
}
