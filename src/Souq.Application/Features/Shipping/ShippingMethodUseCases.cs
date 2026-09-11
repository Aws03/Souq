using FluentValidation;
using MediatR;
using Souq.Application.Common.Models;
using Souq.Application.Common.Tenancy;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Features.Shipping;

// ============================================================================
// طرق الشحن من الإدارة (المرحلة 12، store.shipping.manage): قائمة، إضافة، تعديل، حذف. السعر بعملة المتجر دائماً (لا عملة
// في الطلب). الحذف فعلي: الطلبات تحمل لقطة الطريقة (الاسم والتكلفة والمدّة) لا مفتاحها — ولتعليقها مؤقتاً يُعطَّل.
// ============================================================================
public record ShippingMethodDto(
    int Id, string Name, decimal Price, string Currency, decimal? FreeOverAmount, string? Carrier, string? TrackingUrlTemplate,
    int? MinDays, int? MaxDays, IReadOnlyList<string> Countries, bool IsActive, int SortOrder);

public record ShippingMethodInput(
    string Name, decimal Price, decimal? FreeOverAmount = null, string? Carrier = null, string? TrackingUrlTemplate = null,
    int? MinDays = null, int? MaxDays = null, List<string>? Countries = null, bool IsActive = true, int SortOrder = 0);

public sealed class ShippingMethodInputValidator : AbstractValidator<ShippingMethodInput>
{
    public ShippingMethodInputValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(ShippingMethod.NameMaxLength);
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0);
        RuleFor(x => x.FreeOverAmount).GreaterThan(0).When(x => x.FreeOverAmount is not null);
        RuleFor(x => x.Carrier).MaximumLength(ShippingMethod.CarrierMaxLength);
        RuleFor(x => x.TrackingUrlTemplate).MaximumLength(ShippingMethod.TrackingUrlMaxLength);
        RuleFor(x => x.MinDays).InclusiveBetween(0, ShippingMethod.MaxEstimateDays).When(x => x.MinDays is not null);
        RuleFor(x => x.MaxDays).InclusiveBetween(0, ShippingMethod.MaxEstimateDays).When(x => x.MaxDays is not null);
        RuleFor(x => x.Countries).Must(c => c is null || c.Count <= 60).WithMessage("حتى 60 دولة لطريقة واحدة");
        RuleFor(x => x.SortOrder).InclusiveBetween(0, 10_000);
    }
}

internal static class ShippingMethodMapping
{
    public static ShippingMethodDto ToDto(ShippingMethod m) => new(
        m.Id, m.Name, m.Price.Amount, m.Price.Currency, m.FreeOverAmount, m.Carrier, m.TrackingUrlTemplate, m.MinDays, m.MaxDays,
        m.CountryList, m.IsActive, m.SortOrder);

    public static void Apply(ShippingMethod method, ShippingMethodInput input, string currency)
    {
        method.Update(input.Name, new Money(input.Price, currency), input.FreeOverAmount, input.Carrier, input.TrackingUrlTemplate,
            input.MinDays, input.MaxDays, input.Countries, input.SortOrder);
        if (input.IsActive) method.Activate();
        else method.Deactivate();
    }
}

public record ListShippingMethodsQuery : IRequest<IReadOnlyList<ShippingMethodDto>>;

public class ListShippingMethodsHandler : IRequestHandler<ListShippingMethodsQuery, IReadOnlyList<ShippingMethodDto>>
{
    private readonly IShippingMethodRepository _methods;
    public ListShippingMethodsHandler(IShippingMethodRepository methods) => _methods = methods;

    public async Task<IReadOnlyList<ShippingMethodDto>> Handle(ListShippingMethodsQuery query, CancellationToken ct) =>
        (await _methods.ListAsync(activeOnly: false, ct)).Select(ShippingMethodMapping.ToDto).ToList();
}

public record CreateShippingMethodCommand(ShippingMethodInput Method) : IRequest<Result<int>>;

public sealed class CreateShippingMethodValidator : AbstractValidator<CreateShippingMethodCommand>
{
    public CreateShippingMethodValidator() => RuleFor(x => x.Method).NotNull().SetValidator(new ShippingMethodInputValidator());
}

public class CreateShippingMethodHandler : IRequestHandler<CreateShippingMethodCommand, Result<int>>
{
    private readonly IShippingMethodRepository _methods;
    private readonly ITenantContext _tenant;
    private readonly IUnitOfWork _uow;

    public CreateShippingMethodHandler(IShippingMethodRepository methods, ITenantContext tenant, IUnitOfWork uow)
    {
        _methods = methods; _tenant = tenant; _uow = uow;
    }

    public async Task<Result<int>> Handle(CreateShippingMethodCommand cmd, CancellationToken ct)
    {
        var input = cmd.Method;
        var method = new ShippingMethod(input.Name, new Money(input.Price, _tenant.RequireTenant().Currency), input.FreeOverAmount,
            input.Carrier, input.TrackingUrlTemplate, input.MinDays, input.MaxDays, input.Countries, input.SortOrder);
        if (!input.IsActive) method.Deactivate();
        await _methods.AddAsync(method, ct);
        await _uow.SaveChangesAsync(ct);
        return Result<int>.Success(method.Id);
    }
}

public record UpdateShippingMethodCommand(int Id, ShippingMethodInput Method) : IRequest<Result>;

public sealed class UpdateShippingMethodValidator : AbstractValidator<UpdateShippingMethodCommand>
{
    public UpdateShippingMethodValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Method).NotNull().SetValidator(new ShippingMethodInputValidator());
    }
}

public class UpdateShippingMethodHandler : IRequestHandler<UpdateShippingMethodCommand, Result>
{
    private readonly IShippingMethodRepository _methods;
    private readonly ITenantContext _tenant;
    private readonly IUnitOfWork _uow;

    public UpdateShippingMethodHandler(IShippingMethodRepository methods, ITenantContext tenant, IUnitOfWork uow)
    {
        _methods = methods; _tenant = tenant; _uow = uow;
    }

    public async Task<Result> Handle(UpdateShippingMethodCommand cmd, CancellationToken ct)
    {
        var method = await _methods.GetByIdAsync(cmd.Id, ct);
        if (method is null) return Result.Failure(Error.NotFound("طريقة الشحن غير موجودة"));
        ShippingMethodMapping.Apply(method, cmd.Method, _tenant.RequireTenant().Currency);
        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public record DeleteShippingMethodCommand(int Id) : IRequest<Result>;

public class DeleteShippingMethodHandler : IRequestHandler<DeleteShippingMethodCommand, Result>
{
    private readonly IShippingMethodRepository _methods;
    private readonly IUnitOfWork _uow;

    public DeleteShippingMethodHandler(IShippingMethodRepository methods, IUnitOfWork uow)
    {
        _methods = methods; _uow = uow;
    }

    public async Task<Result> Handle(DeleteShippingMethodCommand cmd, CancellationToken ct)
    {
        var method = await _methods.GetByIdAsync(cmd.Id, ct);
        if (method is null) return Result.Failure(Error.NotFound("طريقة الشحن غير موجودة"));
        _methods.Remove(method);
        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
