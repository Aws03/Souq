using FluentValidation;
using MediatR;
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Models;
using Souq.Application.Common.Tenancy;
using Souq.Domain.Common;
using Souq.Domain.Entities;
using Souq.Domain.Identity;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Auth.Commands;

// تسجيل عميل جديد في متجر المضيف: حساب دخول (User) + ملف شراء (Customer) + جلسة. لا تسجيل ذاتي في
// منطقة المنصّة، ولا يُنشأ مدير أو موظّف من هنا أبداً — رفع الصلاحية ليس تسجيلاً عاماً.
public record RegisterCommand(string FullName, string Email, string Password) : IRequest<Result<AuthSession>>;

public class RegisterValidator : AbstractValidator<RegisterCommand>
{
    public RegisterValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(User.FullNameMaxLength);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(User.EmailMaxLength);
        RuleFor(x => x.Password).StrongPassword();
    }
}

public class RegisterHandler : IRequestHandler<RegisterCommand, Result<AuthSession>>
{
    private readonly IUserRepository _users;
    private readonly ICustomerRepository _customers;
    private readonly IPasswordHasher _hasher;
    private readonly AuthSessionIssuer _sessions;
    private readonly IEmailService _email;
    private readonly IStorefrontLinks _links;
    private readonly ITenantContext _tenant;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public RegisterHandler(
        IUserRepository users, ICustomerRepository customers, IPasswordHasher hasher, AuthSessionIssuer sessions,
        IEmailService email, IStorefrontLinks links, ITenantContext tenant, IUnitOfWork uow, TimeProvider clock)
    {
        _users = users; _customers = customers; _hasher = hasher; _sessions = sessions;
        _email = email; _links = links; _tenant = tenant; _uow = uow; _clock = clock;
    }

    public async Task<Result<AuthSession>> Handle(RegisterCommand cmd, CancellationToken ct)
    {
        if (_tenant.Scope != TenantScope.Tenant)
            return Result<AuthSession>.Failure(Error.Forbidden("RegistrationNotAllowed", "لا تسجيل ذاتي في منطقة المنصّة."));

        // فحص مبكر لرسالة واضحة؛ القيد الفريد (TenantId, NormalizedEmail) هو الحارس الأخير ضد السباق.
        if (await _users.GetByEmailAsync(cmd.Email, ct) is not null)
            return Result<AuthSession>.Failure(Error.Conflict("EmailTaken", "البريد الإلكتروني مستخدم مسبقاً"));

        var user = new User(cmd.FullName, cmd.Email, _hasher.Hash(cmd.Password), Roles.Customer);
        var verificationToken = user.GenerateEmailVerificationToken(_clock.GetUtcNow().UtcDateTime);

        // الحساب ثم ملف العميل (يحتاج معرّفه) ثم الجلسة — وحدة واحدة: لا حساب يبقى بلا ملف شراء.
        var session = await _uow.InTransactionAsync(async () =>
        {
            await _users.AddAsync(user, ct);
            await _uow.SaveChangesAsync(ct);
            await _customers.AddAsync(new Customer(user.Id, user.FullName, user.Email), ct);
            await _uow.SaveChangesAsync(ct);
            return await _sessions.IssueAsync(user, familyId: null, ct);
        }, ct);

        // البريد بعد الالتزام وخارج المعاملة (ADR-0021): فشل المزوّد لا يُسقط التسجيل.
        await _email.SendEmailVerificationAsync(user.Email, _links.EmailVerification(verificationToken), ct);
        return Result<AuthSession>.Success(session);
    }
}
