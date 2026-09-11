using FluentValidation;
using MediatR;
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Models;
using Souq.Application.Common.Security;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Features.Orders.Commands;

// ============================================================================
// إلغاء العميل طلبه (المرحلة 9): صاحبه فقط (غيره 404)، وقبل الدفع فقط (جدول OrderTransitions؛ إلغاء المدفوع عبر المتجر
// والاسترداد في المرحلة 11). طلب له نيّة دفع: نطلب من البوّابة إلغاءها أولاً، خارج أي معاملة (ADR-0021) —
//   نجحت قبل الإلغاء ⇒ يُؤكَّد الدفع ويُرفض الإلغاء (OrderAlreadyPaid)؛
//   ما زالت قيد المعالجة ⇒ يُرفض الآن (PaymentProcessing)؛
//   أُلغيت ⇒ يُلغى الطلب ويُحرَّر حجزه في معاملة واحدة (المسار نفسه لانتهاء المهلة).
// ============================================================================
public record CancelMyOrderCommand(int OrderId, string? Reason = null) : IRequest<Result>;

public class CancelMyOrderValidator : AbstractValidator<CancelMyOrderCommand>
{
    public CancelMyOrderValidator()
    {
        RuleFor(c => c.OrderId).GreaterThan(0);
        RuleFor(c => c.Reason).MaximumLength(300);
    }
}

public class CancelMyOrderHandler : IRequestHandler<CancelMyOrderCommand, Result>
{
    private const string CustomerCancellationNote = "إلغاء من العميل";

    private readonly IOrderRepository _orders;
    private readonly OrderPaymentConfirmation _confirmation;
    private readonly ICurrentUser _currentUser;

    public CancelMyOrderHandler(IOrderRepository orders, OrderPaymentConfirmation confirmation, ICurrentUser currentUser)
    {
        _orders = orders; _confirmation = confirmation; _currentUser = currentUser;
    }

    public async Task<Result> Handle(CancelMyOrderCommand cmd, CancellationToken ct)
    {
        var customerId = _currentUser.RequireCustomerId();
        var order = await _orders.GetWithItemsAsync(cmd.OrderId, ct);
        if (order is null || order.CustomerId != customerId)
            return Result.Failure(Error.NotFound("الطلب غير موجود"));

        if (!OrderTransitions.CustomerCanCancel(order.Status))
            return Result.Failure(Error.BusinessRule("InvalidOrderOperation", "الإلغاء متاح قبل الدفع فقط؛ تواصل مع المتجر لإلغاء طلب مدفوع"));

        return await _confirmation.CancelUnpaidAsync(order,
            string.IsNullOrWhiteSpace(cmd.Reason) ? CustomerCancellationNote : cmd.Reason.Trim(),
            OrderActor.Customer(_currentUser.UserId), ct);
    }
}
