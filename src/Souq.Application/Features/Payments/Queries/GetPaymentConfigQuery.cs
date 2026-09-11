using MediatR;
using Souq.Application.Common.Interfaces;

namespace Souq.Application.Features.Payments.Queries;

// إعدادات الدفع العامة للواجهة (مفتاح Stripe.js العلني). كانت الـ Controller تقرأها من
// IConfiguration مباشرة — الآن تمرّ عبر منفذ الدفع كي لا يعرف الـ API أي مزوّد يعمل.
public record GetPaymentConfigQuery : IRequest<PaymentClientConfig>;

public class GetPaymentConfigHandler : IRequestHandler<GetPaymentConfigQuery, PaymentClientConfig>
{
    private readonly IPaymentService _payment;
    public GetPaymentConfigHandler(IPaymentService payment) => _payment = payment;

    public Task<PaymentClientConfig> Handle(GetPaymentConfigQuery q, CancellationToken ct)
        => Task.FromResult(_payment.GetClientConfig());
}
