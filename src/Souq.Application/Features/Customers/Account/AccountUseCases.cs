using FluentValidation;
using MediatR;
using Souq.Application.Common.Auditing;
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Models;
using Souq.Application.Common.Security;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Features.Customers.Account;

// ============================================================================
// حساب العميل نفسه (المرحلة 7، /api/account): الملف، دفتر العناوين، تصدير بياناته، ومحو حسابه. العميل هو المستخدم
// الحالي دائماً (مطالبة cid) — لا معرّف عميل في أي طلب فلا وصول لملف غيره، ومعرّف عنوان ليس في دفتره ⇒ 404.
// القواعد (الافتراضي، الحدّ، الصيغ) في Customer/PostalAddress؛ هنا التنسيق.
// ============================================================================

internal static class MyCustomer
{
    public static Task<Customer?> LoadAsync(ICustomerRepository customers, ICurrentUser user, CancellationToken ct) =>
        customers.GetByIdAsync(user.RequireCustomerId(), ct);

    public static Error NotFound => Error.NotFound("ملف العميل غير موجود");
    public static Error AddressNotFound => Error.NotFound("العنوان غير موجود");
}

public sealed class AddressInputValidator : AbstractValidator<AddressInput>
{
    public AddressInputValidator()
    {
        RuleFor(x => x.RecipientName).NotEmpty().MaximumLength(PostalAddress.NameMaxLength);
        RuleFor(x => x.Phone).NotEmpty().MaximumLength(PostalAddress.PhoneMaxLength);
        RuleFor(x => x.Country).NotEmpty().Length(2);
        RuleFor(x => x.City).NotEmpty().MaximumLength(PostalAddress.CityMaxLength);
        RuleFor(x => x.Line1).NotEmpty().MaximumLength(PostalAddress.LineMaxLength);
        RuleFor(x => x.Region).MaximumLength(PostalAddress.RegionMaxLength);
        RuleFor(x => x.Line2).MaximumLength(PostalAddress.LineMaxLength);
        RuleFor(x => x.PostalCode).MaximumLength(PostalAddress.PostalCodeMaxLength);
        RuleFor(x => x.Label).MaximumLength(CustomerAddress.LabelMaxLength);
    }
}

// ── الملف ────────────────────────────────────────────────────────────────────

public record GetMyProfileQuery : IRequest<Result<CustomerProfileDto>>;

public class GetMyProfileHandler : IRequestHandler<GetMyProfileQuery, Result<CustomerProfileDto>>
{
    private readonly ICustomerRepository _customers;
    private readonly ICurrentUser _currentUser;

    public GetMyProfileHandler(ICustomerRepository customers, ICurrentUser currentUser)
    {
        _customers = customers; _currentUser = currentUser;
    }

    public async Task<Result<CustomerProfileDto>> Handle(GetMyProfileQuery q, CancellationToken ct) =>
        await MyCustomer.LoadAsync(_customers, _currentUser, ct) is { } customer
            ? Result<CustomerProfileDto>.Success(CustomerProfileDto.From(customer))
            : Result<CustomerProfileDto>.Failure(MyCustomer.NotFound);
}

public record UpdateMyProfileCommand(string FullName, string? Phone) : IRequest<Result<CustomerProfileDto>>;

public sealed class UpdateMyProfileValidator : AbstractValidator<UpdateMyProfileCommand>
{
    public UpdateMyProfileValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(Customer.FullNameMaxLength);
        RuleFor(x => x.Phone).MaximumLength(PostalAddress.PhoneMaxLength);
    }
}

public class UpdateMyProfileHandler : IRequestHandler<UpdateMyProfileCommand, Result<CustomerProfileDto>>
{
    private readonly ICustomerRepository _customers;
    private readonly IUserRepository _users;
    private readonly ICurrentUser _currentUser;
    private readonly IUnitOfWork _uow;

    public UpdateMyProfileHandler(ICustomerRepository customers, IUserRepository users, ICurrentUser currentUser, IUnitOfWork uow)
    {
        _customers = customers; _users = users; _currentUser = currentUser; _uow = uow;
    }

    public async Task<Result<CustomerProfileDto>> Handle(UpdateMyProfileCommand cmd, CancellationToken ct)
    {
        var customer = await MyCustomer.LoadAsync(_customers, _currentUser, ct);
        if (customer is null) return Result<CustomerProfileDto>.Failure(MyCustomer.NotFound);

        customer.UpdateProfile(cmd.FullName, cmd.Phone);
        // اسم الحساب يتبع اسم الملف: الاسم نفسه في رأس الصفحة (/auth/me) وفي رسائل الطلبات.
        (await _users.GetByIdAsync(customer.UserId, ct))?.Rename(cmd.FullName);
        await _uow.SaveChangesAsync(ct);
        return Result<CustomerProfileDto>.Success(CustomerProfileDto.From(customer));
    }
}

// ── دفتر العناوين ──────────────────────────────────────────────────────────

public record ListMyAddressesQuery : IRequest<Result<IReadOnlyList<CustomerAddressDto>>>;

public class ListMyAddressesHandler : IRequestHandler<ListMyAddressesQuery, Result<IReadOnlyList<CustomerAddressDto>>>
{
    private readonly ICustomerRepository _customers;
    private readonly ICurrentUser _currentUser;

    public ListMyAddressesHandler(ICustomerRepository customers, ICurrentUser currentUser)
    {
        _customers = customers; _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<CustomerAddressDto>>> Handle(ListMyAddressesQuery q, CancellationToken ct) =>
        await MyCustomer.LoadAsync(_customers, _currentUser, ct) is { } customer
            ? Result<IReadOnlyList<CustomerAddressDto>>.Success(CustomerAddressDto.Ordered(customer.Addresses))
            : Result<IReadOnlyList<CustomerAddressDto>>.Failure(MyCustomer.NotFound);
}

public record AddMyAddressCommand(AddressInput Address, bool DefaultShipping = false, bool DefaultBilling = false)
    : IRequest<Result<CustomerAddressDto>>;

public sealed class AddMyAddressValidator : AbstractValidator<AddMyAddressCommand>
{
    public AddMyAddressValidator() => RuleFor(x => x.Address).NotNull().SetValidator(new AddressInputValidator());
}

public class AddMyAddressHandler : IRequestHandler<AddMyAddressCommand, Result<CustomerAddressDto>>
{
    private readonly ICustomerRepository _customers;
    private readonly ICurrentUser _currentUser;
    private readonly IUnitOfWork _uow;

    public AddMyAddressHandler(ICustomerRepository customers, ICurrentUser currentUser, IUnitOfWork uow)
    {
        _customers = customers; _currentUser = currentUser; _uow = uow;
    }

    public async Task<Result<CustomerAddressDto>> Handle(AddMyAddressCommand cmd, CancellationToken ct)
    {
        var customer = await MyCustomer.LoadAsync(_customers, _currentUser, ct);
        if (customer is null) return Result<CustomerAddressDto>.Failure(MyCustomer.NotFound);

        var address = customer.AddAddress(cmd.Address.ToDomain(), cmd.Address.Label, cmd.DefaultShipping, cmd.DefaultBilling);
        await _uow.SaveChangesAsync(ct);
        return Result<CustomerAddressDto>.Success(CustomerAddressDto.From(address));
    }
}

public record UpdateMyAddressCommand(int AddressId, AddressInput Address) : IRequest<Result<CustomerAddressDto>>;

public sealed class UpdateMyAddressValidator : AbstractValidator<UpdateMyAddressCommand>
{
    public UpdateMyAddressValidator()
    {
        RuleFor(x => x.AddressId).GreaterThan(0);
        RuleFor(x => x.Address).NotNull().SetValidator(new AddressInputValidator());
    }
}

public class UpdateMyAddressHandler : IRequestHandler<UpdateMyAddressCommand, Result<CustomerAddressDto>>
{
    private readonly ICustomerRepository _customers;
    private readonly ICurrentUser _currentUser;
    private readonly IUnitOfWork _uow;

    public UpdateMyAddressHandler(ICustomerRepository customers, ICurrentUser currentUser, IUnitOfWork uow)
    {
        _customers = customers; _currentUser = currentUser; _uow = uow;
    }

    public async Task<Result<CustomerAddressDto>> Handle(UpdateMyAddressCommand cmd, CancellationToken ct)
    {
        var customer = await MyCustomer.LoadAsync(_customers, _currentUser, ct);
        if (customer is null || customer.Addresses.All(a => a.Id != cmd.AddressId))
            return Result<CustomerAddressDto>.Failure(MyCustomer.AddressNotFound);

        customer.UpdateAddress(cmd.AddressId, cmd.Address.ToDomain(), cmd.Address.Label);
        await _uow.SaveChangesAsync(ct);
        return Result<CustomerAddressDto>.Success(CustomerAddressDto.From(customer.FindAddress(cmd.AddressId)));
    }
}

public record RemoveMyAddressCommand(int AddressId) : IRequest<Result>;

public class RemoveMyAddressHandler : IRequestHandler<RemoveMyAddressCommand, Result>
{
    private readonly ICustomerRepository _customers;
    private readonly ICurrentUser _currentUser;
    private readonly IUnitOfWork _uow;

    public RemoveMyAddressHandler(ICustomerRepository customers, ICurrentUser currentUser, IUnitOfWork uow)
    {
        _customers = customers; _currentUser = currentUser; _uow = uow;
    }

    public async Task<Result> Handle(RemoveMyAddressCommand cmd, CancellationToken ct)
    {
        var customer = await MyCustomer.LoadAsync(_customers, _currentUser, ct);
        if (customer is null || customer.Addresses.All(a => a.Id != cmd.AddressId))
            return Result.Failure(MyCustomer.AddressNotFound);

        customer.RemoveAddress(cmd.AddressId);
        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public enum AddressUse { Shipping, Billing }

public record SetMyDefaultAddressCommand(int AddressId, AddressUse Use) : IRequest<Result>;

public class SetMyDefaultAddressHandler : IRequestHandler<SetMyDefaultAddressCommand, Result>
{
    private readonly ICustomerRepository _customers;
    private readonly ICurrentUser _currentUser;
    private readonly IUnitOfWork _uow;

    public SetMyDefaultAddressHandler(ICustomerRepository customers, ICurrentUser currentUser, IUnitOfWork uow)
    {
        _customers = customers; _currentUser = currentUser; _uow = uow;
    }

    public async Task<Result> Handle(SetMyDefaultAddressCommand cmd, CancellationToken ct)
    {
        var customer = await MyCustomer.LoadAsync(_customers, _currentUser, ct);
        if (customer is null || customer.Addresses.All(a => a.Id != cmd.AddressId))
            return Result.Failure(MyCustomer.AddressNotFound);

        if (cmd.Use == AddressUse.Shipping) customer.SetDefaultShipping(cmd.AddressId);
        else customer.SetDefaultBilling(cmd.AddressId);
        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}

// ── بياناتي: التصدير والمحو ────────────────────────────────────────────────

// نسخة كاملة من بيانات العميل لديه (قابلية نقل البيانات) — مُدقَّقة.
public record ExportMyDataQuery : IRequest<Result<CustomerExportDto>>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("customer.data-exported", "Customer", null,
        Metadata: new Dictionary<string, object?> { ["self"] = true });
}

public class ExportMyDataHandler : IRequestHandler<ExportMyDataQuery, Result<CustomerExportDto>>
{
    private readonly ICustomerQueries _queries;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;

    public ExportMyDataHandler(ICustomerQueries queries, ICurrentUser currentUser, TimeProvider clock)
    {
        _queries = queries; _currentUser = currentUser; _clock = clock;
    }

    public async Task<Result<CustomerExportDto>> Handle(ExportMyDataQuery q, CancellationToken ct) =>
        await _queries.ExportAsync(_currentUser.RequireCustomerId(), _clock.GetUtcNow().UtcDateTime, ct) is { } export
            ? Result<CustomerExportDto>.Success(export)
            : Result<CustomerExportDto>.Failure(MyCustomer.NotFound);
}

// محو الحساب ذاتياً يتطلّب كلمة المرور الحالية: توكن مسروق وحده لا يمحو حساباً.
public record EraseMyAccountCommand(string Password) : IRequest<Result>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("customer.erased", "Customer", null,
        Metadata: new Dictionary<string, object?> { ["self"] = true });
}

public sealed class EraseMyAccountValidator : AbstractValidator<EraseMyAccountCommand>
{
    public EraseMyAccountValidator() => RuleFor(x => x.Password).NotEmpty();
}

public class EraseMyAccountHandler : IRequestHandler<EraseMyAccountCommand, Result>
{
    private readonly ICustomerRepository _customers;
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly CustomerErasure _erasure;
    private readonly ICurrentUser _currentUser;

    public EraseMyAccountHandler(
        ICustomerRepository customers, IUserRepository users, IPasswordHasher hasher, CustomerErasure erasure, ICurrentUser currentUser)
    {
        _customers = customers; _users = users; _hasher = hasher; _erasure = erasure; _currentUser = currentUser;
    }

    public async Task<Result> Handle(EraseMyAccountCommand cmd, CancellationToken ct)
    {
        var customer = await MyCustomer.LoadAsync(_customers, _currentUser, ct);
        if (customer is null) return Result.Failure(MyCustomer.NotFound);

        var user = await _users.GetByIdAsync(customer.UserId, ct);
        if (user is null || !_hasher.Verify(cmd.Password, user.PasswordHash))
            return Result.Failure(Error.Validation("CurrentPasswordIncorrect", "كلمة المرور الحالية غير صحيحة"));

        await _erasure.EraseAsync(customer, ct);
        return Result.Success();
    }
}
