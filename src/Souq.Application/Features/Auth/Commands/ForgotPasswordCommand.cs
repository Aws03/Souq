using MediatR;
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Models;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Auth.Commands;

public record ForgotPasswordCommand(string Email) : IRequest<Result>;

// ============================================================================
// ForgotPasswordHandler — ينجح دائماً بلا استثناء (حتى حين لا يوجد حساب بهذا
// البريد). هذا مقصود: لو أرجعنا خطأً مختلفاً حين لا يوجد الحساب، يستطيع أي زائر
// "تعداد" البريد الإلكتروني المسجَّل بمحاولة عناوين مختلفة ومراقبة الفرق في
// الاستجابة — ثغرة معروفة (User Enumeration). الرسالة الموحّدة تُغلقها كلياً.
// ============================================================================
public class ForgotPasswordHandler : IRequestHandler<ForgotPasswordCommand, Result>
{
    private readonly ICustomerRepository _customers;
    private readonly IEmailService _email;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public ForgotPasswordHandler(ICustomerRepository customers, IEmailService email, IUnitOfWork uow, TimeProvider clock)
    {
        _customers = customers; _email = email; _uow = uow; _clock = clock;
    }

    public async Task<Result> Handle(ForgotPasswordCommand cmd, CancellationToken ct)
    {
        var customer = await _customers.GetByEmailAsync(cmd.Email.Trim().ToLowerInvariant(), ct);
        if (customer is not null)
        {
            var token = customer.GenerateResetToken(_clock.GetUtcNow().UtcDateTime);
            _customers.Update(customer);
            await _uow.SaveChangesAsync(ct);

            await _email.SendPasswordResetEmailAsync(customer.Email, token, ct);
        }

        return Result.Success();
    }
}
