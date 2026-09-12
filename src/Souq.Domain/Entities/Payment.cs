using Souq.Domain.Common;
using Souq.Domain.Enums;
using Souq.Domain.Exceptions;
using Souq.Domain.ValueObjects;

namespace Souq.Domain.Entities;

// ============================================================================
// Payment — دفعة طلب لدى البوّابة (المرحلة 11، ADR-0031): واحدة لكل طلب، تُسجَّل عند إنشاء نيّة الدفع وتُحسم مع الطلب
// (نجاح بالدفع، فشل أو إلغاء بإلغائه). لا بيانات بطاقة هنا أبداً — البطاقة تذهب من المتصفّح إلى البوّابة مباشرة، ونحن
// نحفظ معرّف النيّة لديها فقط.
//
// Gateway: أيّ حساب أنشأ النيّة ("stripe:store" حساب المتجر، "stripe:deployment" حساب النشر، "fake"). التأكيد والإلغاء
// والاسترداد لاحقاً تمرّ بالحساب نفسه، حتى لو غيّر المتجر حسابه بعدها — المال في الحساب الذي قبضه.
//
// الاسترداد تجمّع هنا: مجموع المسترَدّ والمعلّق لا يتجاوز المدفوع. طلب استرداد يزيد PendingRefundAmount فيتغيّر صفّ الدفعة
// نفسه، فيحرسه rowversion: طلبا استرداد متزامنان لا يتجاوزان المبلغ معاً (الثاني يُعاد من قراءة جديدة ويُرفض).
// ============================================================================
public class Payment : Entity, ITenantOwned
{
    public const int GatewayMaxLength = 40;
    public const int ProviderPaymentIdMaxLength = 100;

    public int TenantId { get; private set; }
    public int OrderId { get; private set; }
    public string Gateway { get; private set; } = default!;
    public string ProviderPaymentId { get; private set; } = default!;
    public Money Amount { get; private set; } = default!;
    public PaymentStatus Status { get; private set; }
    public decimal RefundedAmount { get; private set; }
    public decimal PendingRefundAmount { get; private set; }

    private readonly List<Refund> _refunds = new();
    public IReadOnlyCollection<Refund> Refunds => _refunds.AsReadOnly();

    private Payment() { }

    public Payment(int orderId, string gateway, string providerPaymentId, Money amount)
    {
        if (orderId <= 0)
            throw new InvalidPaymentOperationException("الدفعة تخصّ طلباً محفوظاً");
        if (string.IsNullOrWhiteSpace(gateway) || gateway.Length > GatewayMaxLength)
            throw new InvalidPaymentOperationException("حساب البوّابة مطلوب");
        if (string.IsNullOrWhiteSpace(providerPaymentId) || providerPaymentId.Length > ProviderPaymentIdMaxLength)
            throw new InvalidPaymentOperationException("معرّف الدفعة لدى البوّابة مطلوب");

        OrderId = orderId;
        Gateway = gateway;
        ProviderPaymentId = providerPaymentId;
        Amount = amount ?? throw new InvalidPaymentOperationException("مبلغ الدفعة مطلوب");
        Status = PaymentStatus.Pending;
    }

    // ما يمكن استرداده الآن: المدفوع ناقص المسترَدّ والمعلّق.
    public Money Refundable => new(Amount.Amount - RefundedAmount - PendingRefundAmount, Amount.Currency);

    public bool IsFullyRefunded => Status == PaymentStatus.Succeeded && RefundedAmount == Amount.Amount;

    // الحسم مضمون التكرار: تأكيدان (العميل والإشعار) لا يرميان.
    public void MarkSucceeded() => Settle(PaymentStatus.Succeeded);
    public void MarkFailed() => Settle(PaymentStatus.Failed);
    public void MarkCancelled() => Settle(PaymentStatus.Cancelled);

    // البوّابة قبضت المال بعد أن أُغلقت الدفعة: طلب أُلغي ثم نجحت نيّته (R-02). هذا الباب الوحيد الذي ينقض الحسم، لأن
    // البوّابة هي صاحبة الحقيقة في أمر المال: رفضُ تسجيل ما حدث فعلاً يترك المال خارج النظام بلا مسار استرداد —
    // والاسترداد لا يقبل إلا دفعة ناجحة. بعدها تظهر الدفعة ناجحةً على طلب ملغى، وهي إشارة التسوية التي يبحث عنها المشغّل.
    // يعيد ما إذا تغيّر شيء (مضمون التكرار: إشعاران متأخّران لا يسجّلان مرّتين).
    public bool MarkCapturedAfterClose()
    {
        if (Status == PaymentStatus.Succeeded) return false;
        Status = PaymentStatus.Succeeded;
        return true;
    }

    public Refund RequestRefund(Money amount, string? reason, int? requestedByUserId)
    {
        if (Status != PaymentStatus.Succeeded)
            throw new InvalidPaymentOperationException("لا يُستردّ إلا دفع ناجح", "PaymentNotRefundable");
        if (amount.Currency != Amount.Currency)
            throw new InvalidPaymentOperationException($"الاسترداد بعملة الدفعة ({Amount.Currency})");
        if (amount.Amount <= 0)
            throw new InvalidPaymentOperationException("مبلغ الاسترداد يجب أن يكون أكبر من صفر");
        if (amount.Amount > Refundable.Amount)
            throw new InvalidPaymentOperationException(
                $"المبلغ يتجاوز ما يمكن استرداده ({Refundable})", "RefundExceedsPayment");

        var refund = new Refund(amount, reason, requestedByUserId);
        _refunds.Add(refund);
        PendingRefundAmount += amount.Amount;
        return refund;
    }

    // نتيجة البوّابة لاسترداد معلّق. تكرارها (إعادة المحاولة بعد نجاح) لا يعدّ المبلغ مرتين.
    public void CompleteRefund(Refund refund, string providerRefundId, DateTime now)
    {
        EnsureOwn(refund);
        if (!refund.Complete(providerRefundId, now)) return;
        PendingRefundAmount -= refund.Amount.Amount;
        RefundedAmount += refund.Amount.Amount;
    }

    public void FailRefund(Refund refund, string reason, DateTime now)
    {
        EnsureOwn(refund);
        if (!refund.Fail(reason, now)) return;
        PendingRefundAmount -= refund.Amount.Amount;
    }

    private void Settle(PaymentStatus target)
    {
        if (Status == target) return;
        if (Status != PaymentStatus.Pending)
            throw new InvalidPaymentOperationException($"الدفعة محسومة مسبقاً ({Status})");
        Status = target;
    }

    private void EnsureOwn(Refund refund)
    {
        if (!_refunds.Contains(refund))
            throw new InvalidPaymentOperationException("الاسترداد لا يخصّ هذه الدفعة");
    }
}
