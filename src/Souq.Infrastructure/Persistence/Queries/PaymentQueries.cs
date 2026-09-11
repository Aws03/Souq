using Microsoft.EntityFrameworkCore;
using Souq.Application.Features.Payments.Contracts;

namespace Souq.Infrastructure.Persistence.Queries;

// قراءة دفعة طلب باستردادها (المرحلة 11) — إسقاط بلا تتبّع خلف عقد وحدة Payments (ADR-0008).
internal sealed class PaymentQueries : IPaymentQueries
{
    private readonly AppDbContext _db;
    public PaymentQueries(AppDbContext db) => _db = db;

    public async Task<OrderPaymentDto?> ForOrderAsync(int orderId, CancellationToken ct)
    {
        var payment = await _db.Payments.AsNoTracking().Include(p => p.Refunds)
            .FirstOrDefaultAsync(p => p.OrderId == orderId, ct);
        if (payment is null) return null;

        return new OrderPaymentDto(
            payment.Status.ToString(), payment.Amount.Amount, payment.RefundedAmount, payment.Refundable.Amount,
            payment.Amount.Currency,
            payment.Refunds.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id)
                .Select(r => new RefundDto(r.Id, r.Amount.Amount, r.Status.ToString(), r.Reason, r.FailureReason, r.CreatedAt,
                    r.CompletedAt))
                .ToList());
    }
}
