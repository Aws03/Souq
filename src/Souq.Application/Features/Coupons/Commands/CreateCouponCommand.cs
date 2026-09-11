using MediatR;
using Souq.Application.Common.Models;
using Souq.Domain.Enums;

namespace Souq.Application.Features.Coupons.Commands;

// StartsAt وMaxUsesPerCustomer (المرحلة 10): نافذة تبدأ لاحقاً، وحدّ لكل عميل (استخداماته المحجوزة والمؤكَّدة).
public record CreateCouponCommand(
    string Code, DiscountType Type, decimal Value,
    decimal? MinOrderAmount, DateTime? ExpiresAt, int? MaxUses,
    DateTime? StartsAt = null, int? MaxUsesPerCustomer = null
) : IRequest<Result<int>>;
