using MediatR;
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Models;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Auth.Commands;

public record ResetPasswordCommand(string Token, string NewPassword) : IRequest<Result>;

public class ResetPasswordHandler : IRequestHandler<ResetPasswordCommand, Result>
{
    private readonly ICustomerRepository _customers;
    private readonly IPasswordHasher _hasher;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public ResetPasswordHandler(ICustomerRepository customers, IPasswordHasher hasher, IUnitOfWork uow, TimeProvider clock)
    {
        _customers = customers; _hasher = hasher; _uow = uow; _clock = clock;
    }

    public async Task<Result> Handle(ResetPasswordCommand cmd, CancellationToken ct)
    {
        var customer = await _customers.GetByResetTokenAsync(cmd.Token, ct);
        if (customer is null)
            return Result.Failure(Error.BusinessRule("InvalidResetToken", "رابط إعادة التعيين غير صالح"));

        // رمز منتهٍ ⇒ الكيان يرمي InvalidPasswordResetException (422 مركزياً) قبل أي حفظ.
        customer.ResetPassword(_hasher.Hash(cmd.NewPassword), _clock.GetUtcNow().UtcDateTime);
        await _uow.SaveChangesAsync(ct);

        return Result.Success();
    }
}
