using MediatR;
using Souq.Application.Common.Models;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Features.Coupons.Commands;

public class CreateCouponHandler : IRequestHandler<CreateCouponCommand, Result<int>>
{
    private readonly ICouponRepository _coupons;
    private readonly IUnitOfWork _uow;

    public CreateCouponHandler(ICouponRepository coupons, IUnitOfWork uow)
    {
        _coupons = coupons; _uow = uow;
    }

    public async Task<Result<int>> Handle(CreateCouponCommand cmd, CancellationToken ct)
    {
        if (await _coupons.GetByCodeAsync(cmd.Code, ct) is not null)
            return Result<int>.Failure(Error.Conflict("DuplicateCode", "رمز الكوبون مستخدم بالفعل"));

        // قيم غير منطقية (نسبة خارج 1-100) ⇒ الكيان يرمي InvalidCouponException (422 مركزياً).
        var coupon = new Coupon(cmd.Code, cmd.Type, cmd.Value,
            cmd.MinOrderAmount.HasValue ? new Money(cmd.MinOrderAmount.Value) : null,
            cmd.ExpiresAt, cmd.MaxUses);

        await _coupons.AddAsync(coupon, ct);
        await _uow.SaveChangesAsync(ct);
        return Result<int>.Success(coupon.Id);
    }
}
