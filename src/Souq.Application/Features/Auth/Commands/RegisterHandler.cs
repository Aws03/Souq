using MediatR;
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Models;
using Souq.Domain.Common;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Auth.Commands;

// ينسّق التسجيل: يمنع تكرار البريد، يجزّئ كلمة المرور، ينشئ العميل بدور Customer،
// ثم يُصدر توكناً. الأدمن لا يُنشأ من هنا (يُبذَر فقط) — رفع الصلاحية ليس تسجيلاً عاماً.
public class RegisterHandler : IRequestHandler<RegisterCommand, Result<AuthResponse>>
{
    private readonly ICustomerRepository _customers;
    private readonly IPasswordHasher _hasher;
    private readonly IJwtTokenGenerator _jwt;
    private readonly IUnitOfWork _uow;

    public RegisterHandler(ICustomerRepository customers, IPasswordHasher hasher,
        IJwtTokenGenerator jwt, IUnitOfWork uow)
    {
        _customers = customers; _hasher = hasher; _jwt = jwt; _uow = uow;
    }

    public async Task<Result<AuthResponse>> Handle(RegisterCommand cmd, CancellationToken ct)
    {
        // فحص مبكر للبريد المكرّر لرسالة واضحة. القيد الفريد في قاعدة البيانات هو
        // الحارس النهائي ضد التسابق بين طلبين متزامنين.
        var existing = await _customers.GetByEmailAsync(cmd.Email, ct);
        if (existing is not null)
            return Result<AuthResponse>.Failure("البريد الإلكتروني مستخدم مسبقاً", "EmailTaken");

        var customer = new Customer(
            cmd.FullName.Trim(), cmd.Email.Trim().ToLowerInvariant(),
            _hasher.Hash(cmd.Password), Roles.Customer);

        await _customers.AddAsync(customer, ct);
        await _uow.SaveChangesAsync(ct);

        var (token, expires) = _jwt.Generate(customer);
        return Result<AuthResponse>.Success(new AuthResponse(token, expires,
            new UserInfo(customer.Id, customer.FullName, customer.Email, customer.Role)));
    }
}
