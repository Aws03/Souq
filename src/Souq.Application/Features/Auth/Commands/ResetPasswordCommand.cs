using MediatR;
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Models;
using Souq.Domain.Exceptions;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Auth.Commands;

public record ResetPasswordCommand(string Token, string NewPassword) : IRequest<Result>;

public class ResetPasswordHandler : IRequestHandler<ResetPasswordCommand, Result>
{
    private readonly ICustomerRepository _customers;
    private readonly IPasswordHasher _hasher;
    private readonly IUnitOfWork _uow;

    public ResetPasswordHandler(ICustomerRepository customers, IPasswordHasher hasher, IUnitOfWork uow)
    {
        _customers = customers; _hasher = hasher; _uow = uow;
    }

    public async Task<Result> Handle(ResetPasswordCommand cmd, CancellationToken ct)
    {
        var customer = await _customers.GetByResetTokenAsync(cmd.Token, ct);
        if (customer is null)
            return Result.Failure("رابط إعادة التعيين غير صالح", "InvalidToken");

        try { customer.ResetPassword(_hasher.Hash(cmd.NewPassword)); }
        catch (InvalidPasswordResetException ex) { return Result.Failure(ex.Message, "TokenExpired"); }

        _customers.Update(customer);
        await _uow.SaveChangesAsync(ct);

        return Result.Success();
    }
}
