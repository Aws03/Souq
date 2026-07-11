using MediatR;
using Souq.Application.Common.Models;
using Souq.Domain.Enums;

namespace Souq.Application.Features.Coupons.Commands;

public record CreateCouponCommand(
    string Code, DiscountType Type, decimal Value,
    decimal? MinOrderAmount, DateTime? ExpiresAt, int? MaxUses
) : IRequest<Result<int>>;
