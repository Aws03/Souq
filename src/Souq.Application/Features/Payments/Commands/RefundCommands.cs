using FluentValidation;
using MediatR;
using Souq.Application.Common.Auditing;
using Souq.Application.Common.Models;
using Souq.Application.Common.Security;
using Souq.Application.Features.Payments.Contracts;
using Souq.Domain.Entities;

namespace Souq.Application.Features.Payments.Commands;

// ============================================================================
// الاسترداد من الإدارة (المرحلة 11، store.payments.manage): جزئي بمبلغ، أو كل المتبقّي بلا مبلغ. طلب متجر آخر لا دفعة له
// في هذا المتجر ⇒ 404. الموظّف الطالب يُسجَّل على الاسترداد وفي سجلّ التدقيق.
// ============================================================================
public record RefundOrderCommand(int OrderId, decimal? Amount, string? Reason) : IRequest<Result<RefundOutcome>>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("order.refund.requested", "Order", OrderId.ToString(),
        Metadata: new Dictionary<string, object?> { ["amount"] = Amount });
}

public sealed class RefundOrderValidator : AbstractValidator<RefundOrderCommand>
{
    public RefundOrderValidator()
    {
        RuleFor(x => x.OrderId).GreaterThan(0);
        RuleFor(x => x.Amount).GreaterThan(0).When(x => x.Amount is not null);
        RuleFor(x => x.Reason).MaximumLength(Refund.ReasonMaxLength);
    }
}

public class RefundOrderHandler : IRequestHandler<RefundOrderCommand, Result<RefundOutcome>>
{
    private readonly IOrderPayments _payments;
    private readonly ICurrentUser _currentUser;

    public RefundOrderHandler(IOrderPayments payments, ICurrentUser currentUser)
    {
        _payments = payments; _currentUser = currentUser;
    }

    public Task<Result<RefundOutcome>> Handle(RefundOrderCommand cmd, CancellationToken ct) =>
        _payments.RefundAsync(cmd.OrderId, cmd.Amount, cmd.Reason, _currentUser.RequireUserId(), ct);
}

// إعادة استرداد معلّق (لم تُجب البوّابة): المفتاح نفسه، فإن كان الطلب الأول قد وصلها أعادت نتيجته ولا تردّ مرتين.
public record RetryRefundCommand(int OrderId, int RefundId) : IRequest<Result<RefundOutcome>>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("order.refund.retried", "Order", OrderId.ToString(),
        Metadata: new Dictionary<string, object?> { ["refundId"] = RefundId });
}

public class RetryRefundHandler : IRequestHandler<RetryRefundCommand, Result<RefundOutcome>>
{
    private readonly IOrderPayments _payments;
    public RetryRefundHandler(IOrderPayments payments) => _payments = payments;

    public Task<Result<RefundOutcome>> Handle(RetryRefundCommand cmd, CancellationToken ct) =>
        _payments.RetryRefundAsync(cmd.OrderId, cmd.RefundId, ct);
}
