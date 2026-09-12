using MediatR;
using Microsoft.Extensions.Logging;
using Souq.Application.Common.Exceptions;
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Models;
using Souq.Application.Common.Tenancy;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Orders.Commands;

// ============================================================================
// ProcessPaymentWebhookCommand — حالة استخدام في وحدة Ordering (لا Payments): وحدة الطلبات تعتمد على عقد الدفع، لا العكس
// (Modules.md — لا دورات). البوّابة تتحقّق من التوقيع وتستخرج مرجع الطلب ومتجره؛ ثم نفس منطق التأكيد الذي يستخدمه العميل
// (OrderPaymentConfirmation) — يعيد التحقّق لدى البوّابة ومضمون التكرار. التفويض هنا هو التوقيع لا مستخدم.
//
// التوجيه للمتجر الصحيح (المرحلة 11): حساب النشر المشترك يرسل إشعارات كل متاجره إلى رابط واحد، فالحدث قد يصل على مضيف
// متجر غير متجره — يُطبَّق عندئذ داخل نطاق المتجر المسمّى في بيانات النيّة (ITenantScopeRunner). حدث وقّعه حساب متجر
// نفسه لا يُوجَّه لغيره أبداً. التطبيق في النطاق الهدف يعيد السؤال لدى البوّابة، فادّعاء متجر لا يغيّر شيئاً وحده.
// ============================================================================
public record ProcessPaymentWebhookCommand(string Payload, string? Signature) : IRequest<Result>;

public class ProcessPaymentWebhookHandler : IRequestHandler<ProcessPaymentWebhookCommand, Result>
{
    private readonly IPaymentService _payment;
    private readonly ITenantContext _tenant;
    private readonly ITenantDirectory _directory;
    private readonly ITenantScopeRunner _scopes;
    private readonly IMediator _mediator;
    private readonly ILogger<ProcessPaymentWebhookHandler> _logger;

    public ProcessPaymentWebhookHandler(
        IPaymentService payment, ITenantContext tenant, ITenantDirectory directory, ITenantScopeRunner scopes, IMediator mediator,
        ILogger<ProcessPaymentWebhookHandler> logger)
    {
        _payment = payment; _tenant = tenant; _directory = directory; _scopes = scopes; _mediator = mediator; _logger = logger;
    }

    public async Task<Result> Handle(ProcessPaymentWebhookCommand cmd, CancellationToken ct)
    {
        PaymentWebhookEvent? evt;
        try { evt = await _payment.ParseWebhookAsync(cmd.Payload, cmd.Signature, ct); }
        catch (InvalidPaymentWebhookException ex) { return Result.Failure(Error.Validation("InvalidSignature", ex.Message)); }

        // حدث لا يخصّ دفعة طلب (أو لا سرّ إشعارات مضبوط) — نُقرّ بالاستلام دون فعل شيء.
        if (evt is null) return Result.Success();

        var here = _tenant.RequireTenant();
        var target = evt.TenantId ?? here.Id;
        if (target == here.Id)
            return await _mediator.Send(new ApplyPaymentEventCommand(evt.OrderReference), ct);

        if (evt.VerifiedByStoreAccount)
        {
            _logger.LogWarning("Webhook signed by store {TenantId}'s own account names store {TargetId}; ignored", here.Id, target);
            return Result.Success();
        }

        var store = await _directory.FindByIdAsync(target, ct);
        if (store is null)
        {
            _logger.LogWarning("Payment webhook names unknown store {TargetId}; acknowledged without action", target);
            return Result.Success();
        }

        return await _scopes.RunAsync<IMediator, Result>(store,
            mediator => mediator.Send(new ApplyPaymentEventCommand(evt.OrderReference), ct));
    }
}

// تطبيق حدث دفعة على طلب متجر السياق: التحقّق لدى البوّابة نفسها ثم التأكيد المضمون التكرار. داخلي — لا نقطة HTTP تصله؛
// يرسله معالج الإشعار في نطاق المتجر المعني.
public record ApplyPaymentEventCommand(string OrderReference) : IRequest<Result>;

public class ApplyPaymentEventHandler : IRequestHandler<ApplyPaymentEventCommand, Result>
{
    private readonly IOrderRepository _orders;
    private readonly OrderPaymentConfirmation _confirmation;
    private readonly ILogger<ApplyPaymentEventHandler> _logger;

    public ApplyPaymentEventHandler(IOrderRepository orders, OrderPaymentConfirmation confirmation, ILogger<ApplyPaymentEventHandler> logger)
    {
        _orders = orders; _confirmation = confirmation; _logger = logger;
    }

    public async Task<Result> Handle(ApplyPaymentEventCommand cmd, CancellationToken ct)
    {
        if (!int.TryParse(cmd.OrderReference, out var orderId)) return Result.Success();

        var order = await _orders.GetWithItemsAsync(orderId, ct);
        if (order is null)
        {
            _logger.LogWarning("Payment webhook referenced unknown order {OrderId}; acknowledged without action", orderId);
            return Result.Success();
        }

        // نتيجة التأكيد (نجاح/فشل دفع) تُطبَّق على الطلب؛ للبوّابة يكفي أننا عالجنا الحدث — إقرار بالاستلام كي لا تُعيد
        // الإرسال إلى الأبد. ما لم يُطبَّق يُسجَّل برمزه: PaymentCapturedOnCancelledOrder يعني مالاً يحتاج تسوية (R-02).
        var applied = await _confirmation.ConfirmAsync(order, ct);
        if (!applied.IsSuccess)
            _logger.LogWarning("Payment event for order {OrderId} was acknowledged without confirming it: {ErrorCode}",
                orderId, applied.Error!.Code);
        return Result.Success();
    }
}
